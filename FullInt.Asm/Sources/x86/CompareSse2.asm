; =============================================================================
;  x86 (32-bit) compare with SSE2 (Pentium 4, Core 2 and later), next to Compare.asm (the
;  baseline, kept as is); picked at start-up when Fullint64CpuFeatures (x86/Cpu.asm) reports
;  SSE2. cdecl. Same contract:
;
;    int Fullint64CompareWIN_sse2(A, B, int lenA, int lenB)   -> -1, 0, 1  (|A| vs |B|)
;
;  Equal lengths (the engine's case: its quick check decides different lengths and top words
;  itself), from the top; the first word that differs decides (leading zero words do not
;  matter at equal lengths):
;    eight words a pass: pcmpeqd of four 16-byte pairs, the masks and-ed, one pmovmskb -
;    the baseline compares a dword at a time (1.3-1.5x GMP from 32 words);
;    then four words, then single words; a block that differs: its words one by one.
;  Different lengths: a tail jump into the baseline kernel with the arguments untouched
;  (it skips leading zeros).
;
;  Registers: esi / edi walk A / B down from the top (index 0 = the most significant word),
;  ecx words left, eax scratch; xmm0-7.
; =============================================================================

%include "abi86.inc"
CODE_SECTION

EXTERNK Fullint64CompareWIN

ENTRY Fullint64CompareWIN_sse2
	mov ecx, [esp + 12]           ; lenA
	cmp ecx, [esp + 16]           ; lenB
	jne KREF(Fullint64CompareWIN) ; different lengths: the baseline kernel
	push esi
	push edi
	mov esi, [esp + 12]           ; A
	mov edi, [esp + 16]           ; B
	align 16
.eights:
	cmp ecx, 8
	jb .four
	movdqu xmm0, [esi]
	movdqu xmm1, [esi + 16]
	movdqu xmm2, [esi + 32]
	movdqu xmm3, [esi + 48]
	movdqu xmm4, [edi]
	movdqu xmm5, [edi + 16]
	movdqu xmm6, [edi + 32]
	movdqu xmm7, [edi + 48]
	pcmpeqd xmm0, xmm4
	pcmpeqd xmm1, xmm5
	pcmpeqd xmm2, xmm6
	pcmpeqd xmm3, xmm7
	pand xmm0, xmm1
	pand xmm2, xmm3
	pand xmm0, xmm2
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .block8
	add esi, 64
	add edi, 64
	sub ecx, 8
	jmp .eights
.four:
	cmp ecx, 4
	jb .words
	movdqu xmm0, [esi]
	movdqu xmm1, [esi + 16]
	movdqu xmm4, [edi]
	movdqu xmm5, [edi + 16]
	pcmpeqd xmm0, xmm4
	pcmpeqd xmm1, xmm5
	pand xmm0, xmm1
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .block4
	add esi, 32
	add edi, 32
	sub ecx, 4
	jmp .words
.block8:                          ; a word of these eight differs: find it
	mov ecx, 8
	jmp .words
.block4:
	mov ecx, 4
.words:                           ; ecx words, one at a time (high dword first)
	test ecx, ecx
	jz .equal
.word:
	mov eax, [esi + 4]
	cmp eax, [edi + 4]
	jne .decide
	mov eax, [esi]
	cmp eax, [edi]
	jne .decide
	add esi, 8
	add edi, 8
	dec ecx
	jnz .word
.equal:
	xor eax, eax
	pop edi
	pop esi
	ret
.decide:                          ; CF = A < B (unsigned)
	sbb eax, eax
	or eax, 1
	pop edi
	pop esi
	ret

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
