
; =============================================================================
;  x86 (32-bit) port of x64/Add.asm: the same three runs, 32-bit registers.
;  cdecl, all arguments on the stack; ebx, esi, edi, ebp are saved.
;
;  ulong Fullint64AddWIN(A, B, R, int lenA, int lenB, int lenR)   -> edx:eax
;
;  Limb order: index 0 = most significant 64-bit word. A 64-bit word sits in memory
;  as two dwords, the low one first, so each limb is two adc steps: [p], then [p + 4].
;
;  Register map (x64 -> x86)
;    rdi / r10  A          -> esi (end of A, then base of the run)
;    rsi / r11  B          -> edi
;    rcx / r12  R          -> ebp
;    r8         index      -> ecx (k1-1 .. 0 in run 1, k2-1 .. 0 in run 2; jecxz instead of jrcxz)
;    rdx        k2         -> stack [esp+4]
;    r9         k3         -> stack [esp+8]
;    rax        -k2        -> stack [esp], loaded into edx for the lea
;    r13        temp       -> eax
; =============================================================================

%include "abi86.inc"
section .text

ENTRY Fullint64AddWIN
	push ebp
	push ebx
	push esi
	push edi
	mov esi, [esp+20]             ; A
	mov edi, [esp+24]             ; B
	mov ebp, [esp+28]             ; R
	mov edx, [esp+32]             ; lenA
	mov ecx, [esp+36]             ; lenB
	mov ebx, [esp+40]             ; lenR

	; lengths are resolved once here, so the loops below have no per-word range checks.
	; The result is walked from its lowest word up in three runs:
	;   1) k1 = min(lenB, lenR) words of A + B + carry      (carry stays in CF: dec/lea/mov keep it)
	;   2) k2 = min(lenA, lenR) - k1 words of A + carry     (the longer operand's tail)
	;   3) k3 = lenR - min(lenA, lenR) words: the carry, then zeros
	; returns the carry out of the top word of R (0 when R is longer than both operands)
	cmp edx, ecx
	jae .ordered
	xchg esi, edi                 ; A is the longer operand (add commutes)
	xchg edx, ecx
.ordered:
	lea esi, [esi + edx*8]        ; end of A (past its lowest word)
	lea edi, [edi + ecx*8]        ; end of B
	lea ebp, [ebp + ebx*8]        ; end of R
	cmp ecx, ebx
	cmova ecx, ebx                ; k1 = min(lenB, lenR)
	cmp edx, ebx
	cmova edx, ebx                ; min(lenA, lenR)
	sub ebx, edx                  ; k3
	sub edx, ecx                  ; k2
	push ebx                      ; [esp+8] k3
	push edx                      ; [esp+4] k2
	neg edx
	push edx                      ; [esp]   -k2, moves the bases to run 2 without touching flags
	lea eax, [ecx*8]
	sub esi, eax                  ; bases = top word of run 1, index ecx runs k1-1 .. 0 (low word first)
	sub edi, eax
	sub ebp, eax
	dec ecx
	clc

.addPairs:                        ; 1) A + B
	mov eax, [esi + ecx*8]
	adc eax, [edi + ecx*8]
	mov [ebp + ecx*8], eax
	mov eax, [esi + ecx*8 + 4]
	adc eax, [edi + ecx*8 + 4]
	mov [ebp + ecx*8 + 4], eax
	dec ecx
	jns .addPairs

	mov ecx, [esp+4]              ; k2 (tested by jecxz, which keeps CF)
	jecxz .tail
	mov edx, [esp]                ; -k2
	lea esi, [esi + edx*8]        ; bases = top word of run 2
	lea ebp, [ebp + edx*8]
	lea ecx, [ecx - 1]
.addCarry:                        ; 2) A + carry
	mov eax, [esi + ecx*8]
	adc eax, 0
	mov [ebp + ecx*8], eax
	mov eax, [esi + ecx*8 + 4]
	adc eax, 0
	mov [ebp + ecx*8 + 4], eax
	dec ecx
	jns .addCarry

.tail:
	setc al
	movzx eax, al                 ; carry out of the operands
	mov ebx, [esp+8]              ; k3
	test ebx, ebx
	jz .done
	mov [ebp - 8], eax            ; 3) the carry, then zeros
	mov dword [ebp - 4], 0
	xor eax, eax
	sub ebp, 8
	dec ebx
	jz .done
.zeros:
	sub ebp, 8
	mov [ebp], eax
	mov [ebp + 4], eax
	dec ebx
	jnz .zeros

.done:
	add esp, 12
	xor edx, edx                  ; ulong result: edx:eax
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
