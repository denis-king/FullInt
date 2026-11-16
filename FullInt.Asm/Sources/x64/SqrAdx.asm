default rel
section .text

; =============================================================================
;  R = AÂ² with mulx / adcx / adox (BMI2 + ADX: Broadwell, Zen and later). A new kernel next to
;  MultAdx.asm; picked at start-up when Fullint64CpuFeatures reports BMI2 and ADX (without
;  them the callers square through the multiplication).
;
;  Limb order: index 0 = most significant word (as every kernel here).
;  Windows: void Fullint64SqrWIN_adx(A, R, int lenA, int lenR)     lenR >= 2Â·lenA, R != A;
;  the words of R above 2Â·lenA are zero.
;
;  Method (indices from the low end): AÂ² = 2Â·T + D,
;    T = sum over i < j of a_iÂ·a_jÂ·Î²^(i+j)   (n(n-1)/2 products instead of nÂ²)
;    D = sum a_iÂ²Â·Î²^(2i)
;  T by rows as MultAdx: row i adds a_iÂ·(a_(i+1) .. a_(n-1)) at r_(2i+1), the row's carry word
;  r_(i+n) is new (R cleared first):
;    mulx  hi, lo, a_j        ; rdx = a_i
;    adcx  lo, prev_hi        ; chain 1 (CF)
;    adox  lo, r_(i+j)        ; chain 2 (OF)
;  then one pass over the pairs r_(2k), r_(2k+1): doubled on the CF chain (adcx x, x), a_kÂ²
;  added on the OF chain. Loop control by lea / jrcxz only (no flags).
;
;  Registers: r10 a_0, r11 r_0, r15 n; rows: rbx a_i, r13 r_(2i+1), r12 the row's length
;  n - 1 - i, rcx single words then groups of eight, r14 the groups, rsi a_j, rdi r_(i+j),
;  rdx a_i, r8 prev_hi, r9 hi, rax lo.
; =============================================================================

global Fullint64SqrWIN_adx
Fullint64SqrWIN_adx:
	push rbx                    ; Win64 non-volatile, used by the body
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15

	mov r10, rcx                ; A
	mov r11, rdx                ; R
	movsxd r15, r8d             ; n
	movsxd r9, r9d              ; lenR
	mov rdi, r11                ; R cleared: the rows add into it, their top words are new
	mov rcx, r9                 ; (a store loop: rep stosq's start-up, ~30 cycles, was a fifth
	xor eax, eax                ; of a square of 4 words; lenR is at most 2·SqrThreshold here)
.clear:
	mov [rdi], rax
	lea rdi, [rdi + 8]
	dec rcx
	jnz .clear
	lea r10, [r10 + r15*8 - 8]  ; a_0
	lea r11, [r11 + r9*8 - 8]   ; r_0
	lea r12, [r15 - 1]          ; row 0: n - 1 words
	mov rbx, r10                ; a_i
	lea r13, [r11 - 8]          ; r_(2i+1)

.row:
	test r12, r12
	jz .diagonal
	mov rdx, [rbx]              ; a_i
	lea rsi, [rbx - 8]          ; a_(i+1)
	mov rdi, r13
	mov rcx, r12
	and rcx, 7                  ; single words first
	mov r14, r12
	shr r14, 3                  ; then groups of eight (0.88-0.95 of four at 64-256 words, Zen 3)
	xor r8d, r8d                ; prev_hi = 0; clears CF and OF
.word:
	jrcxz .quads
	mulx r9, rax, [rsi]         ; r9:rax = a_j * a_i
	adcx rax, r8                ; + prev_hi + CF
	adox rax, [rdi]             ; + r_(i+j) + OF
	mov [rdi], rax
	mov r8, r9
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	lea rcx, [rcx - 1]
	jmp .word
.quads:
	mov rcx, r14
.quad:
	jrcxz .qout
	jmp .qbody
.qout:
	jmp .rowEnd
.qbody:
	mulx r9, rax, [rsi]
	adcx rax, r8
	adox rax, [rdi]
	mov [rdi], rax
	mulx r8, rax, [rsi - 8]
	adcx rax, r9
	adox rax, [rdi - 8]
	mov [rdi - 8], rax
	mulx r9, rax, [rsi - 16]
	adcx rax, r8
	adox rax, [rdi - 16]
	mov [rdi - 16], rax
	mulx r8, rax, [rsi - 24]
	adcx rax, r9
	adox rax, [rdi - 24]
	mov [rdi - 24], rax
	mulx r9, rax, [rsi - 32]
	adcx rax, r8
	adox rax, [rdi - 32]
	mov [rdi - 32], rax
	mulx r8, rax, [rsi - 40]
	adcx rax, r9
	adox rax, [rdi - 40]
	mov [rdi - 40], rax
	mulx r9, rax, [rsi - 48]
	adcx rax, r8
	adox rax, [rdi - 48]
	mov [rdi - 48], rax
	mulx r8, rax, [rsi - 56]
	adcx rax, r9
	adox rax, [rdi - 56]
	mov [rdi - 56], rax
	lea rsi, [rsi - 64]
	lea rdi, [rdi - 64]
	lea rcx, [rcx - 1]
	jmp .quad
.rowEnd:
	mov eax, 0                  ; (mov: the flags stay)
	adcx r8, rax                ; + CF
	adox r8, rax                ; + OF
	mov [rdi], r8               ; r_(i+n): not written by earlier rows
	lea rbx, [rbx - 8]          ; a_(i+1)
	lea r13, [r13 - 16]         ; r_(2i+3)
	dec r12
	jmp .row

.diagonal:                      ; R = 2Â·T + D, the pairs r_(2k), r_(2k+1)
	mov rcx, r15
	mov rsi, r10
	mov rdi, r11
	xor eax, eax                ; clears CF and OF
.pair:
	jrcxz .done
	mov rdx, [rsi]
	mulx r9, rax, rdx           ; a_kÂ²
	mov r8, [rdi]
	mov r12, [rdi - 8]
	adcx r8, r8                 ; doubled: chain 1
	adcx r12, r12
	adox r8, rax                ; + a_kÂ²: chain 2
	adox r12, r9
	mov [rdi], r8
	mov [rdi - 8], r12
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 16]
	lea rcx, [rcx - 1]
	jmp .pair

.done:
	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	ret
