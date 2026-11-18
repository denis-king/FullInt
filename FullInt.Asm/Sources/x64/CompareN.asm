default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  Compare of two magnitudes of the same length (the engine's quick check has decided the
;  lengths and the top words): no lengths, no CPU check - SSE2 only (every x86-64), so no
;  vzeroupper either.
;
;    int Fullint64CmpNWIN(const uint64_t* A, const uint64_t* B, int64 n)   -> -1, 0, 1
;      n >= 1, index 0 = the most significant word; the first word that differs decides.
;
;    1-2 words: one at a time;
;    3-4 words: [0, 2) and [n-2, n) at once (two 16-byte compares);
;    5-8 words: the top four and the bottom four (overlapping below 8) at once - eight 16-byte
;               loads, pcmpeqd, the masks and-ed, one pmovmskb;
;    9+:        eight words a pass the same way, then the last eight as 4-8 above (overlapping).
;  A block that differs: its words one by one from its top.
;
;  Registers (Win64 volatile only): rcx A, rdx B, r8 n, r9 offset, rax, r10, r11; xmm0-5.
; =============================================================================

ENTRY Fullint64CmpNWIN
	WIN64_ARGS
fi_cmpn_body:                     ; rcx A, rdx B, r8 n (Win64 registers): CmpObj comes in here
	cmp r8, 3
	jb .words
	xor r9d, r9d                  ; the block's first word
	cmp r8, 4
	ja .four
	movdqu xmm0, [rcx]            ; 3-4 words: [0, 2) and [n-2, n)
	movdqu xmm1, [rcx + r8*8 - 16]
	movdqu xmm2, [rdx]
	movdqu xmm3, [rdx + r8*8 - 16]
	pcmpeqd xmm0, xmm2
	pcmpeqd xmm1, xmm3
	pand xmm0, xmm1
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .find
	xor eax, eax
	WRET
.four:
	cmp r8, 8
	ja .eights
.last:                            ; r9 .. n-1 with n - r9 in 4..8: [r9, r9+4) and [n-4, n)
	lea r10, [r8*8 - 32]
	movdqu xmm0, [rcx + r9*8]
	movdqu xmm1, [rcx + r9*8 + 16]
	movdqu xmm2, [rcx + r10]
	movdqu xmm3, [rcx + r10 + 16]
	movdqu xmm4, [rdx + r9*8]
	movdqu xmm5, [rdx + r9*8 + 16]
	pcmpeqd xmm0, xmm4
	pcmpeqd xmm1, xmm5
	movdqu xmm4, [rdx + r10]
	movdqu xmm5, [rdx + r10 + 16]
	pcmpeqd xmm2, xmm4
	pcmpeqd xmm3, xmm5
	pand xmm0, xmm1
	pand xmm2, xmm3
	pand xmm0, xmm2
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .find
	xor eax, eax
	WRET

	align 16
.eights:                          ; while more than 8 words are left: eight a pass
	movdqu xmm0, [rcx + r9*8]
	movdqu xmm1, [rcx + r9*8 + 16]
	movdqu xmm2, [rcx + r9*8 + 32]
	movdqu xmm3, [rcx + r9*8 + 48]
	movdqu xmm4, [rdx + r9*8]
	movdqu xmm5, [rdx + r9*8 + 16]
	pcmpeqd xmm0, xmm4
	pcmpeqd xmm1, xmm5
	movdqu xmm4, [rdx + r9*8 + 32]
	movdqu xmm5, [rdx + r9*8 + 48]
	pcmpeqd xmm2, xmm4
	pcmpeqd xmm3, xmm5
	pand xmm0, xmm1
	pand xmm2, xmm3
	pand xmm0, xmm2
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .find
	add r9, 8
	lea rax, [r9 + 8]
	cmp rax, r8
	jb .eights
	lea r9, [r8 - 8]              ; 1-8 words left: the last eight (overlapping what is done)
	jmp .last

.find:                            ; a word from r9 on differs
	mov rax, [rcx + r9*8]
	cmp rax, [rdx + r9*8]
	jne .decide
	inc r9
	jmp .find

.words:                           ; 1-3 words
	xor r9d, r9d
.word:
	mov rax, [rcx + r9*8]
	cmp rax, [rdx + r9*8]
	jne .decide
	inc r9
	cmp r9, r8
	jne .word
	xor eax, eax
	WRET
.decide:                          ; CF = A < B
	sbb eax, eax
	or eax, 1
	WRET

;
;    int Fullint64CmpObjWIN(const FullInt64* a, const FullInt64* b)   -> -1, 0, 1  (a vs b, signed)
;      the whole C++ Compare on the objects (FullInt64: +0 the inline words / the heap pointer when
;      cap > 8, +40h cap, +44h off, +48h size, +4Ch neg; normalized): the signs, the lengths, the
;      top words, then the words as above; a negative pair's answer turned round. One call and
;      nothing around it in the caller (the inline compare's frame and branches cost 4 words
;      1.3x GMP).
ENTRY Fullint64CmpObjWIN
	WIN64_ARGS
	movzx eax, byte [rcx + 4Ch]   ; a's sign
	movzx r8d, byte [rdx + 4Ch]
	cmp eax, r8d
	jne .signs
	mov r8d, [rcx + 48h]          ; la
	mov r9d, [rdx + 48h]          ; lb
	cmp r8d, r9d
	jne .lengths
	mov r9d, eax                  ; the sign (r9 free now)
	mov r10, [rcx]                ; the heap pointer or the first inline word (read either way)
	mov r11, [rdx]
	cmp dword [rcx + 40h], 8
	cmovbe r10, rcx               ; inline: the object itself
	cmp dword [rdx + 40h], 8
	cmovbe r11, rdx
	mov eax, [rcx + 44h]
	lea rcx, [r10 + rax*8]        ; a's words
	mov eax, [rdx + 44h]
	lea rdx, [r11 + rax*8]        ; b's words
	mov rax, [rcx]
	cmp rax, [rdx]
	jne .oDecide                  ; the top words decide (the common case)
	cmp r8, 1
	je .oEqual
	cmp r8, 4
	ja .oLonger
	movdqu xmm0, [rcx]            ; 2-4 words: [0, 2) and [n-2, n)
	movdqu xmm1, [rcx + r8*8 - 16]
	movdqu xmm2, [rdx]
	movdqu xmm3, [rdx + r8*8 - 16]
	pcmpeqd xmm0, xmm2
	pcmpeqd xmm1, xmm3
	pand xmm0, xmm1
	pmovmskb eax, xmm0
	cmp eax, 0FFFFh
	jne .oFind
.oEqual:
	xor eax, eax
	WRET
.oLonger:                         ; 5+: the unsigned compare above (a jump, its return is this one's);
	test r9d, r9d                 ; both negative: |b| against |a| is the signed answer
	jz fi_cmpn_body
	xchg rcx, rdx
	jmp fi_cmpn_body
.oFind:
	mov r10d, 1
.oNext:
	mov rax, [rcx + r10*8]
	cmp rax, [rdx + r10*8]
	jne .oDecide
	inc r10
	jmp .oNext
.oDecide:                         ; CF = |a| < |b|; a negative pair: turned round
	sbb eax, eax
	or eax, 1
	neg r9d
	xor eax, r9d
	sub eax, r9d
	WRET
.signs:                           ; different signs: the negative one is smaller
	neg eax
	lea eax, [rax*2 + 1]          ; a negative: -1, else 1
	WRET
.lengths:                         ; CF = la < lb; the longer is larger in magnitude
	sbb r8d, r8d
	or r8d, 1
	neg eax
	xor r8d, eax
	sub r8d, eax
	mov eax, r8d
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
