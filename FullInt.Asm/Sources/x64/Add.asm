
default rel
%include "abi.inc"
CODE_SECTION

; the entry (abi.inc): the arguments to the body's registers, then a jump over the padding
ENTRY Fullint64AddWIN
	SAVE rdi, rsi, r12, r13, r14
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d
	movsxd r8, dword STACKARG(5, FI_NPUSH)
	movsxd r9, dword STACKARG(6, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd rdx, edx
	movsxd r8, r8d
	movsxd r9, r9d
%endif
	jmp Fullint64Add_body

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW

; Code placement: keep the body at offset 1Ch mod 64 (where it was before the
; entry grew). The hot loop is placement-sensitive: other offsets measured
; 10-30% slower on Zen 3. Padding sits after the entry's jmp, never executed.
	times (1Ch - ($ - $$)) & 63 int3
Fullint64Add_body:
	; lengths are resolved once here, so the loops below have no per-word range checks.
	; The result is walked from its lowest word up in three runs:
	;   1) k1 = min(lenB, lenR) words of A + B + carry      (carry stays in CF: dec/lea/mov keep it)
	;   2) k2 = min(lenA, lenR) - k1 words of A + carry     (the longer operand's tail)
	;   3) k3 = lenR - min(lenA, lenR) words: the carry, then zeros
	; returns the carry out of the top word of R (0 when R is longer than both operands)
	cmp rdx, r8
	jae .ordered
	xchg rdi, rsi                 ; A is the longer operand (add commutes)
	xchg rdx, r8
.ordered:
	lea r10, [rdi + rdx*8]        ; end of A (past its lowest word)
	lea r11, [rsi + r8*8]         ; end of B
	lea r12, [rcx + r9*8]         ; end of R
	cmp r8, r9
	cmova r8, r9                  ; k1 = min(lenB, lenR)
	cmp rdx, r9
	cmova rdx, r9                 ; min(lenA, lenR)
	sub r9, rdx                   ; k3
	sub rdx, r8                   ; k2
	mov rax, rdx
	neg rax                       ; rax = -k2, moves the bases to run 2 without touching flags
	lea r13, [r8*8]
	sub r10, r13                  ; bases = top word of run 1, index r8 runs k1-1 .. 0 (low word first)
	sub r11, r13
	sub r12, r13
	mov rcx, r8
	shr rcx, 2                    ; groups of four
	mov r14, r8
	and r14, 3                    ; single words first
	dec r8
	test r14, r14                 ; (flags tested before the chain: clc keeps ZF)
	clc
	jz .addGroups
.addSingle:                      ; 1) A + B, the low k1 mod 4 words
	mov r13, [r10 + r8*8]
	adc r13, [r11 + r8*8]
	mov [r12 + r8*8], r13
	dec r8
	dec r14                       ; (dec keeps CF)
	jnz .addSingle
.addGroups:
	jrcxz .addRun1Done
.addGroup:                       ; four words a pass: 0.8x of one a pass at 64-256 words (Zen 3)
	mov r13, [r10 + r8*8]
	adc r13, [r11 + r8*8]
	mov [r12 + r8*8], r13
	mov r13, [r10 + r8*8 - 8]
	adc r13, [r11 + r8*8 - 8]
	mov [r12 + r8*8 - 8], r13
	mov r13, [r10 + r8*8 - 16]
	adc r13, [r11 + r8*8 - 16]
	mov [r12 + r8*8 - 16], r13
	mov r13, [r10 + r8*8 - 24]
	adc r13, [r11 + r8*8 - 24]
	mov [r12 + r8*8 - 24], r13
	lea r8, [r8 - 4]
	dec rcx
	jnz .addGroup
.addRun1Done:
	mov rcx, rdx                  ; rcx = k2 (tested by jrcxz, which keeps CF)
	jrcxz .tail
	lea r10, [r10 + rax*8]        ; bases = top word of run 2
	lea r12, [r12 + rax*8]
	lea rcx, [rcx - 1]
.addCarry:                        ; 2) A + carry
	mov r13, [r10 + rcx*8]
	adc r13, 0
	mov [r12 + rcx*8], r13
	dec rcx
	jns .addCarry

.tail:
	setc al
	movzx eax, al                 ; carry out of the operands
	test r9, r9
	jz .done
	mov [r12 - 8], rax            ; 3) the carry, then zeros
	xor eax, eax
	sub r12, 8
	dec r9
	jz .done
.zeros:
	sub r12, 8
	mov [r12], rax
	dec r9
	jnz .zeros

.done:
    ARET rdi, rsi, r12, r13, r14

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
