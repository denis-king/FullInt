
default rel
section .text align=64

;WINDOWS wrapper
global Fullint64AddWIN
Fullint64AddWIN:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push r12
	push r13
	push r14
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d             ; lenA (int)
	movsxd r8, dword [rsp+50h]  ; lenB (int; 28h + 5 pushes)
	movsxd r9, dword [rsp+58h]  ; lenR (int; 30h + 5 pushes)

	call Fullint64Add
	pop r14
	pop r13
	pop r12
	pop rsi
	pop rdi
	ret

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW

; Code placement: keep the body at offset 1Ch mod 64 (where it was before the
; wrapper grew). The hot loop is placement-sensitive: other offsets measured
; 10-30% slower on Zen 3. Padding sits after the wrapper's ret, never executed.
	times (1Ch - ($ - $$)) & 63 int3
global Fullint64Add
Fullint64Add:
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
	mov rcx, rdx                  ; rcx = k2 (tested by jrcxz, which keeps CF)
	mov rax, rdx
	neg rax                       ; rax = -k2, moves the bases to run 2 without touching flags
	lea r13, [r8*8]
	sub r10, r13                  ; bases = top word of run 1, index r8 runs k1-1 .. 0 (low word first)
	sub r11, r13
	sub r12, r13
	dec r8
	clc

.addPairs:                        ; 1) A + B
	mov r13, [r10 + r8*8]
	adc r13, [r11 + r8*8]
	mov [r12 + r8*8], r13
	dec r8
	jns .addPairs

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
    ret

; Code placement: end this file at offset 30h mod 64, where it ended before the loop rewrite,
; so every function linked after it (Compare, Div, Mult, ...) keeps its offset in the 64-byte
; lines. Without this, all of them moved by 16 bytes and Div measured ~5% slower. Never executed.
	times (30h - ($ - $$)) & 63 int3