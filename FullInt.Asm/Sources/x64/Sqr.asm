default rel
%include "abi.inc"
section .text

; =============================================================================
;  R = A², baseline x86-64 (mul / adc: Core 2 and later), for the CPUs without BMI2 + ADX
;  (SqrAdx.asm otherwise). A new kernel: the original multiply (Mult.asm) is not touched.
;
;  Windows: void Fullint64SqrWIN(A, R, int lenA, int lenR)     lenR >= 2·lenA, R != A;
;  the words of R above 2·lenA are zero. Limb order: index 0 = most significant word.
;
;  A² = 2·T + D (indices from the low end):
;    T = sum over i < j of a_i·a_j·β^(i+j): rows, row i adds a_i·(a_(i+1) .. a_(n-1)) at
;        r_(2i+1) with the carry in a register (mul clobbers the flags), the row's top word
;        r_(i+n) is new (R cleared first);
;    R doubled: one adc pass (dec keeps CF);
;    D = sum a_i²·β^(2i) added: one pass, the carry in a register.
;
;  Registers: r10 a_0, r11 r_0, r12 n; rows: r14 a_i, r9 r_(2i+1), r13 the row's length,
;  rbx a_i, rsi a_j, rdi r_(i+j), rcx words left, r8 the carry word, rdx:rax the product.
; =============================================================================
ENTRY Fullint64SqrWIN
	WIN64_ARGS
	push rbx                    ; Win64 non-volatile, used by the body
	push rsi
	push rdi
	push r12
	push r13
	push r14

	mov r10, rcx                ; A
	mov r11, rdx                ; R
	movsxd r12, r8d             ; n
	movsxd r9, r9d              ; lenR
	mov rdi, r11                ; R cleared: the rows add into it, their top words are new
	mov rcx, r9
	xor eax, eax
	rep stosq
	lea r10, [r10 + r12*8 - 8]  ; a_0
	lea r11, [r11 + r9*8 - 8]   ; r_0
	lea r13, [r12 - 1]          ; row 0: n - 1 words
	mov r14, r10                ; a_i
	lea r9, [r11 - 8]           ; r_(2i+1)

.row:
	test r13, r13
	jz .double
	mov rbx, [r14]              ; a_i
	test rbx, rbx
	jz .rowNext                 ; a zero word adds nothing (its top word stays 0)
	lea rsi, [r14 - 8]          ; a_(i+1)
	mov rdi, r9
	mov rcx, r13
	xor r8d, r8d
.word:                          ; r_(i+j) added before the carry word: off the carry chain
	mov rax, [rsi]
	mul rbx                     ; rdx:rax = a_j·a_i
	add rax, [rdi]
	adc rdx, 0
	add rax, r8                 ; the chain: r8 -> add -> adc -> r8
	adc rdx, 0
	mov [rdi], rax
	mov r8, rdx
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	dec rcx
	jnz .word
	mov [rdi], r8               ; r_(i+n)
.rowNext:
	lea r14, [r14 - 8]          ; a_(i+1)
	lea r9, [r9 - 16]           ; r_(2i+3)
	dec r13
	jmp .row

.double:                        ; R = 2·T: the 2n words, one adc chain
	mov rdi, r11
	lea rcx, [r12 + r12]
	clc
.dbl:
	mov rax, [rdi]
	adc rax, rax
	mov [rdi], rax
	lea rdi, [rdi - 8]
	dec rcx                     ; (CF stays)
	jnz .dbl

	; + D: a_k² into r_(2k), r_(2k+1), the carry in rbx (0 / 1)
	mov rsi, r10
	mov rdi, r11
	mov rcx, r12
	xor ebx, ebx
.square:
	mov rax, [rsi]
	mul rax                     ; a_k²
	add rax, rbx
	adc rdx, 0                  ; (no carry out: a_k² < β² - 2β + 2)
	add [rdi], rax
	adc [rdi - 8], rdx
	sbb rbx, rbx
	neg rbx
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 16]
	dec rcx
	jnz .square

	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	WRET
