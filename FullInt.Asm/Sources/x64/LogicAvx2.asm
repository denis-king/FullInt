
default rel
%include "abi.inc"
section .text

; =============================================================================
;  Bitwise AND / OR / XOR with AVX2 (Haswell, Zen and later; OS ymm support).
;  A new kernel next to Logic.asm (the baseline, kept as is); picked at start-up when
;  Fullint64CpuFeatures reports AVX2.
;
;  Same contract as Fullint64AndWIN etc. (Logic.asm):
;    int Fullint64AndWIN_avx2(A, B, R, int lenA, int lenB, int lenR, int signs)
;  lenR >= max(lenA, lenB) + 1; returns 1 when the result is negative.
;
;  Both operands non-negative (signs = 0, the common case): no two's complement carries, the
;  operation is word-wise - four words per vpand / vpor / vpxor, from the low end:
;    the overlap (the shorter length):  R = A op B
;    the longer operand's other words:  AND: zero;  OR / XOR: copied
;    the words above:                   zero
;  A negative operand: a tail jump into the baseline kernel with the arguments untouched
;  (nothing pushed yet), which converts two's complement on the fly as before.
;
;  Registers: rcx / rdx / r8 walk down A / B / R from their ends; r9 overlap words,
;  r10 the longer operand's other words, r11 the zero words above; rsi = 0: those come from
;  A, 1: from B; ymm0 only (xmm6-15 are non-volatile in Win64).
; =============================================================================

extern Fullint64AndWIN
extern Fullint64OrWIN
extern Fullint64XorWIN

%macro BITWISE_AVX2 4 ; %1 name, %2 vector op, %3 scalar op, %4 1: the longer operand's other words are copied (OR, XOR), 0: zero (AND)
ENTRY Fullint64%1WIN_avx2
%ifndef FULLINT_WIN64
	cmp dword [rsp+8], 0        ; System V: signs (argument 7) before the frame
	jne Fullint64%1WIN          ; a negative operand: the baseline kernel (its own entry)
%endif
	WIN64_ARGS
	cmp dword [rsp+38h], 0      ; signs
	jne Fullint64%1WIN          ; a negative operand: the baseline kernel

	movsxd r9, r9d              ; lenA
	movsxd r10, dword [rsp+28h] ; lenB
	movsxd r11, dword [rsp+30h] ; lenR
	lea rcx, [rcx + r9*8]       ; ends (one past the lowest word)
	lea rdx, [rdx + r10*8]
	lea r8, [r8 + r11*8]
	push rsi                    ; Win64 non-volatile

	mov rax, r9
	sub rax, r10                ; lenA - lenB
	jb %%bLonger
	sub r11, r9                 ; words above = lenR - lenA
	mov r9, r10                 ; overlap = lenB
	mov r10, rax                ; A's other words
	xor esi, esi
	jmp %%blocks
%%bLonger:
	neg rax
	sub r11, r10                ; words above = lenR - lenB
	mov r10, rax                ; B's other words (overlap = lenA, already in r9)
	mov esi, 1

%%blocks:                       ; the overlap, four words at a time
	cmp r9, 4
	jb %%words
	sub rcx, 32
	sub rdx, 32
	sub r8, 32
	vmovdqu ymm0, [rcx]
	%2 ymm0, ymm0, [rdx]
	vmovdqu [r8], ymm0
	sub r9, 4
	jmp %%blocks
%%words:
	test r9, r9
	jz %%rest
	sub rcx, 8
	sub rdx, 8
	sub r8, 8
	mov rax, [rcx]
	%3 rax, [rdx]
	mov [r8], rax
	dec r9
	jmp %%words

%%rest:
%if %4
	test esi, esi
	cmovnz rcx, rdx             ; the longer operand
%%copyBlocks:
	cmp r10, 4
	jb %%copyWords
	sub rcx, 32
	sub r8, 32
	vmovdqu ymm0, [rcx]
	vmovdqu [r8], ymm0
	sub r10, 4
	jmp %%copyBlocks
%%copyWords:
	test r10, r10
	jz %%above
	sub rcx, 8
	sub r8, 8
	mov rax, [rcx]
	mov [r8], rax
	dec r10
	jmp %%copyWords
%else
	add r11, r10                ; AND: the longer operand's other words are zero as well
%endif

%%above:                        ; r11 >= 1 zero words up to R's top
	vpxor ymm0, ymm0, ymm0
%%zeroBlocks:
	cmp r11, 4
	jb %%zeroWords
	sub r8, 32
	vmovdqu [r8], ymm0
	sub r11, 4
	jmp %%zeroBlocks
%%zeroWords:
	test r11, r11
	jz %%done
	sub r8, 8
	mov qword [r8], 0
	dec r11
	jmp %%zeroWords

%%done:
	vzeroupper
	pop rsi
	xor eax, eax                ; non-negative result
	WRET
%endmacro

BITWISE_AVX2 And, vpand, and, 0
BITWISE_AVX2 Or,  vpor,  or,  1
BITWISE_AVX2 Xor, vpxor, xor, 1
