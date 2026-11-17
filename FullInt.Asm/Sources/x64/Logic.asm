
default rel
%include "abi.inc"
section .text

; =============================================================================
;  Bitwise AND / OR / XOR with BigInteger semantics (two's complement view of
;  negative numbers) on sign-magnitude operands.
;  Limb order: index 0 = most significant word (same as the rest of FullInt).
;
;  A, B   magnitudes, lenA / lenB words (>= 1)
;  signs  bit0 = A is negative, bit1 = B is negative
;  R      lenR words, lenR >= max(lenA, lenB) + 1; receives the result magnitude
;  return eax = 1 if the result is negative, 0 otherwise
;
;  One pass from LSW to MSW. Every word goes through
;      t = (word XOR mask) + carry          mask = 0 or all-ones, carry starts at mask & 1
;  which is two's complement on the fly (identity for non-negative operands).
;  The result is converted back to a magnitude the same way with maskR = op(maskA, maskB).
;
;  int Fullint64AndWIN(A, B, R, int lenA, int lenB, int lenR, int signs)   (Windows and System V:
;  one entry, abi.inc; the body takes the arguments as Fullint64Add's, signs in r10d)
;
;  Body registers
;    rdi A        rdx A words left      rbx maskA   r12 carryA
;    rsi B        r8  B words left      r13 maskB   r14 carryB
;    rcx R        r9  R words left      r15 maskR   r10 carryR
;    rax / r11 current A / B word
; =============================================================================

%macro BITWISE_OP 2 ; %1 = name suffix, %2 = instruction (and / or / xor)


ENTRY Fullint64%1WIN
	SAVE rdi, rsi
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d
	movsxd r8, dword STACKARG(5, FI_NPUSH)
	movsxd r9, dword STACKARG(6, FI_NPUSH)
	mov r10d, dword STACKARG(7, FI_NPUSH)      ; signs
%else
	xchg rcx, rdx
	movsxd rdx, edx
	movsxd r8, r8d
	movsxd r9, r9d
	mov r10d, dword STACKARG(7, FI_NPUSH)      ; signs
%endif
Fullint64%1_body:
	push rbx                    ; System V non-volatile
	push r12
	push r13
	push r14
	push r15

	mov ebx, r10d
	and ebx, 1
	mov r12d, ebx               ; carryA = signA
	neg rbx                     ; maskA = signA ? all-ones : 0
	mov r13d, r10d
	shr r13d, 1
	and r13d, 1
	mov r14d, r13d              ; carryB = signB
	neg r13                     ; maskB
	mov r15, rbx
	%2 r15, r13                 ; maskR = op(maskA, maskB)
	mov r10d, r15d
	and r10d, 1                 ; carryR = signR

%%loop:
	; A word (0 above the top) → two's complement
	xor eax, eax
	test rdx, rdx
	jz %%haveA
	mov rax, [rdi + rdx*8 - 8]
	dec rdx
%%haveA:
	xor rax, rbx
	add rax, r12
	setc r12b

	; B word → two's complement
	xor r11d, r11d
	test r8, r8
	jz %%haveB
	mov r11, [rsi + r8*8 - 8]
	dec r8
%%haveB:
	xor r11, r13
	add r11, r14
	setc r14b

	%2 rax, r11

	; result → magnitude
	xor rax, r15
	add rax, r10
	setc r10b
	mov [rcx + r9*8 - 8], rax
	dec r9
	jnz %%loop

	mov eax, r15d
	and eax, 1                  ; result sign

	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	ARET rdi, rsi
%endmacro

BITWISE_OP And, and
BITWISE_OP Or,  or
BITWISE_OP Xor, xor
