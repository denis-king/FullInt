default rel
%include "abi.inc"
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
;  n < 10 (.small): T in registers (rows unrolled for each n, no R word read back while T is built),
;  then 2T + D unrolled. Zen 3: 2 words 3.8 ns (mpn_sqr 5.7, the rows 6.2), 4: 6.6 (7.1, 13.8),
;  8: 19.4 (24.6, 35).
;  10 <= n <= 17 (.mid): T = T(A0) + T(A1)·β^2h + A1·A0·β^h (h = n / 2), each part in registers as
;  .small, then 2T + D. 10 words 28 ns (mpn_sqr 37, the rows 47), 16: 61 (83, 90), 17: 68 (93, 96).
;
;  Registers: r10 a_0, r11 r_0, r15 n; rows: rbx a_i, r13 r_(2i+1), r12 the row's length
;  n - 1 - i, rcx single words then groups of eight, r14 the groups, rsi a_j, rdi r_(i+j),
;  rdx a_i, r8 prev_hi, r9 hi, rax lo.
; =============================================================================
ENTRY Fullint64SqrWIN_adx
	WIN64_ARGS
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
	cmp r15, 10
	jb .small
	cmp r15, 18
	jb .mid
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
	WRET

	; n < 8: the triangle T in registers (rows as in .row, the words being added in a window that rotates
	; through W[p mod n]; positions 2i+1, 2i+2 are final after row i: stored, then zeroed - mov keeps the
	; flags - the first one the zero for the row's last OF; a row ends with CF = OF = 0), then 2T + D pair by
	; pair, unrolled. Words of R above 2n zeroed here (the clearing above is for the longer squares).
	; Registers: r10 a_0, r11 r_0, rdx a_i, rax hi, rcx lo, r8 r9 a_k^2, the window rbx rsi rdi r12 r13 r14 r15
.small:
	mov rdi, r11
	lea rcx, [r15 + r15]
	sub rcx, r9                 ; - (lenR - 2n)
	jz .smallGo
	xor eax, eax
.smallZero:
	mov [rdi], rax
	lea rdi, [rdi + 8]
	inc rcx
	jnz .smallZero
.smallGo:
	lea r10, [r10 + r15*8 - 8]  ; a_0
	lea r11, [r11 + r9*8 - 8]   ; r_0
	lea rcx, [rel .smallTbl]
	mov rax, [rcx + r15*8 - 8]
	add rax, rcx
	jmp rax
.s1:
	xor ebx, ebx
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov [r11 - 8], r9
	jmp .done
.s2:
	xor ebx, ebx
	xor esi, esi
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rbx, rax
	mov [r11 - 8], rsi
	mov esi, 0
	adox rbx, rsi
	mov [r11 - 16], rbx
	mov ebx, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	jmp .done
.s3:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 24], rbx
	mov ebx, 0
	adox rsi, rbx
	mov [r11 - 32], rsi
	mov esi, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	jmp .done
.s4:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx rbx, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], rbx
	mov ebx, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 40], rsi
	mov esi, 0
	adox rdi, rsi
	mov [r11 - 48], rdi
	mov edi, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	jmp .done
.s5:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 40], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, rbx
	mov [r11 - 48], rsi
	mov esi, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 56], rdi
	mov edi, 0
	adox r12, rdi
	mov [r11 - 64], r12
	mov r12d, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	jmp .done
.s6:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx rbx, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], rbx
	mov ebx, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 56], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rsi
	mov [r11 - 64], rdi
	mov edi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 72], r12
	mov r12d, 0
	adox r13, r12
	mov [r11 - 80], r13
	mov r13d, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	jmp .done
.s7:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 56], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rbx
	mov [r11 - 64], rsi
	mov esi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 72], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, rdi
	mov [r11 - 80], r12
	mov r12d, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 88], r13
	mov r13d, 0
	adox r14, r13
	mov [r11 - 96], r14
	mov r14d, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	jmp .done
.s8:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx rbx, rax
	mov [r11 - 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 64], rbx
	mov ebx, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 72], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	adox r13, rsi
	mov [r11 - 80], rdi
	mov edi, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 88], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	adox r14, r12
	mov [r11 - 96], r13
	mov r13d, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 104], r14
	mov r14d, 0
	adox r15, r14
	mov [r11 - 112], r15
	mov r15d, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	jmp .done
.s9:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	xor r9d, r9d
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 64]
	adox r9, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 56]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 64]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 48]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 64]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx r9, rax
	mov [r11 - 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 40]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 64]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 64], r9
	mov r9d, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 72], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 64]
	adox r12, rcx
	adcx r13, rax
	adox r13, rbx
	mov [r11 - 80], rsi
	mov esi, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 88], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 64]
	adox r13, rcx
	adcx r14, rax
	adox r14, rdi
	mov [r11 - 96], r12
	mov r12d, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 104], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 64]
	adox r14, rcx
	adcx r15, rax
	adox r15, r13
	mov [r11 - 112], r14
	mov r14d, 0
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox r15, rcx
	adcx r8, rax
	mov [r11 - 120], r15
	mov r15d, 0
	adox r8, r15
	mov [r11 - 128], r8
	mov r8d, 0
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov [r11], r8
	mov rcx, [r11 - 8]
	adcx rcx, rcx
	adox rcx, r9
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov ecx, 0
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	jmp .done

	; 10 <= n <= 17: A = A1·β^h + A0 (h = n / 2, both halves at most 8 words), T = T(A0) + T(A1)·β^2h
	; + A1·A0·β^h: the two triangles in registers as .small (their positions cover R), A1·A0 in registers
	; (a window of n - h + 1) to the stack, added at r_h, then 2T + D. No R word read back while a part
	; is built.
.mid:
	mov rdi, r11
	lea rcx, [r15 + r15]
	sub rcx, r9                 ; - (lenR - 2n)
	jz .midGo
	xor eax, eax
.midZero:
	mov [rdi], rax
	lea rdi, [rdi + 8]
	inc rcx
	jnz .midZero
.midGo:
	lea r10, [r10 + r15*8 - 8]  ; a_0
	lea r11, [r11 + r9*8 - 8]   ; r_0
	sub rsp, 136                ; A1·A0 (n words)
	lea rcx, [rel .midTbl]
	mov rax, [rcx + r15*8 - 80]
	add rax, rcx
	jmp rax
.n10:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	mov [r11], rbx
	mov [r11 - 72], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 40], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, rbx
	mov [r11 - 48], rsi
	mov esi, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 56], rdi
	mov edi, 0
	adox r12, rdi
	mov [r11 - 64], r12
	mov r12d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	mov [r11 - 80], rbx
	mov [r11 - 152], rbx
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 88], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 64]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 72]
	adox r13, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 96], rdi
	mov edi, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 104], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 64]
	adox r13, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 72]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 112], r13
	mov r13d, 0
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 120], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 72]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, rbx
	mov [r11 - 128], rsi
	mov esi, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 136], rdi
	mov edi, 0
	adox r12, rdi
	mov [r11 - 144], r12
	mov r12d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	adox r14, rbx
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov [rsp + 40], r14
	mov [rsp + 48], rbx
	mov [rsp + 56], rsi
	mov [rsp + 64], rdi
	mov [rsp + 72], r12
	mov rax, [rsp]
	add rax, [r11 - 40]
	mov [r11 - 40], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 48]
	mov [r11 - 48], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	adc qword [r11 - 120], 0
	adc qword [r11 - 128], 0
	adc qword [r11 - 136], 0
	adc qword [r11 - 144], 0
	adc qword [r11 - 152], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	add rsp, 136
	jmp .done
.n11:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	mov [r11], rbx
	mov [r11 - 72], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 40], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, rbx
	mov [r11 - 48], rsi
	mov esi, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 56], rdi
	mov edi, 0
	adox r12, rdi
	mov [r11 - 64], r12
	mov r12d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov [r11 - 80], rbx
	mov [r11 - 168], rbx
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 88], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 64]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 72]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 80]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 96], rdi
	mov edi, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 104], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 64]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 72]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 80]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 112], r13
	mov r13d, 0
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox r14, rcx
	adcx rbx, rax
	mov [r11 - 120], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 72]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 80]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 128], rbx
	mov ebx, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 136], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 80]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rsi
	mov [r11 - 144], rdi
	mov edi, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 152], r12
	mov r12d, 0
	adox r13, r12
	mov [r11 - 160], r13
	mov r13d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	adox r14, rbx
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx rbx, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 16]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 24]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 32]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov [rsp + 48], rbx
	mov [rsp + 56], rsi
	mov [rsp + 64], rdi
	mov [rsp + 72], r12
	mov [rsp + 80], r13
	mov rax, [rsp]
	add rax, [r11 - 40]
	mov [r11 - 40], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 48]
	mov [r11 - 48], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	adc qword [r11 - 128], 0
	adc qword [r11 - 136], 0
	adc qword [r11 - 144], 0
	adc qword [r11 - 152], 0
	adc qword [r11 - 160], 0
	adc qword [r11 - 168], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	add rsp, 136
	jmp .done
.n12:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov [r11], rbx
	mov [r11 - 88], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx rbx, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], rbx
	mov ebx, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 56], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rsi
	mov [r11 - 64], rdi
	mov edi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 72], r12
	mov r12d, 0
	adox r13, r12
	mov [r11 - 80], r13
	mov r13d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov [r11 - 96], rbx
	mov [r11 - 184], rbx
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 104], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 64]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 72]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 80]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 112], rdi
	mov edi, 0
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 120], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 72]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 80]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 88]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 128], r13
	mov r13d, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox r14, rcx
	adcx rbx, rax
	mov [r11 - 136], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 80]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 88]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 144], rbx
	mov ebx, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 152], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 88]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rsi
	mov [r11 - 160], rdi
	mov edi, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 168], r12
	mov r12d, 0
	adox r13, r12
	mov [r11 - 176], r13
	mov r13d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	adox r15, rbx
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov [rsp + 48], r15
	mov [rsp + 56], rbx
	mov [rsp + 64], rsi
	mov [rsp + 72], rdi
	mov [rsp + 80], r12
	mov [rsp + 88], r13
	mov rax, [rsp]
	add rax, [r11 - 48]
	mov [r11 - 48], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	adc qword [r11 - 144], 0
	adc qword [r11 - 152], 0
	adc qword [r11 - 160], 0
	adc qword [r11 - 168], 0
	adc qword [r11 - 176], 0
	adc qword [r11 - 184], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	add rsp, 136
	jmp .done
.n13:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	mov [r11], rbx
	mov [r11 - 88], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx rbx, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], rbx
	mov ebx, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 56], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rsi
	mov [r11 - 64], rdi
	mov edi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 72], r12
	mov r12d, 0
	adox r13, r12
	mov [r11 - 80], r13
	mov r13d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov [r11 - 96], rbx
	mov [r11 - 200], rbx
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 104], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 64]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 72]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 80]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 96]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 112], rdi
	mov edi, 0
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 120], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 72]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 80]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 88]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 96]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 128], r13
	mov r13d, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 136], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 80]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 88]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 96]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 144], r15
	mov r15d, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 152], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 88]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 96]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rbx
	mov [r11 - 160], rsi
	mov esi, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 168], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 96]
	adox r12, rcx
	adcx r13, rax
	adox r13, rdi
	mov [r11 - 176], r12
	mov r12d, 0
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10 - 96]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 184], r13
	mov r13d, 0
	adox r14, r13
	mov [r11 - 192], r14
	mov r14d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	adox r15, rbx
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10]
	adox r15, rcx
	adcx rbx, rax
	mov [rsp + 48], r15
	mov r15d, 0
	mulx rax, rcx, [r10 - 8]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 16]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 24]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 32]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 40]
	adox r13, rcx
	adcx r14, rax
	adox r14, r15
	mov [rsp + 56], rbx
	mov [rsp + 64], rsi
	mov [rsp + 72], rdi
	mov [rsp + 80], r12
	mov [rsp + 88], r13
	mov [rsp + 96], r14
	mov rax, [rsp]
	add rax, [r11 - 48]
	mov [r11 - 48], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	mov rax, [rsp + 96]
	adc rax, [r11 - 144]
	mov [r11 - 144], rax
	adc qword [r11 - 152], 0
	adc qword [r11 - 160], 0
	adc qword [r11 - 168], 0
	adc qword [r11 - 176], 0
	adc qword [r11 - 184], 0
	adc qword [r11 - 192], 0
	adc qword [r11 - 200], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	mov rdx, [r10 - 96]
	mulx r9, r8, rdx
	mov rax, [r11 - 192]
	mov rcx, [r11 - 200]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 192], rax
	mov [r11 - 200], rcx
	add rsp, 136
	jmp .done
.n14:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov [r11], rbx
	mov [r11 - 104], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 56], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rbx
	mov [r11 - 64], rsi
	mov esi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 72], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, rdi
	mov [r11 - 80], r12
	mov r12d, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 88], r13
	mov r13d, 0
	adox r14, r13
	mov [r11 - 96], r14
	mov r14d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov [r11 - 112], rbx
	mov [r11 - 216], rbx
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 120], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 72]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 80]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 88]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 96]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 104]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 128], rdi
	mov edi, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 136], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 80]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 96]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 104]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 144], r13
	mov r13d, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 152], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 88]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 96]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 104]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 160], r15
	mov r15d, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 168], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 96]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 104]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rbx
	mov [r11 - 176], rsi
	mov esi, 0
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10 - 96]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 184], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 104]
	adox r12, rcx
	adcx r13, rax
	adox r13, rdi
	mov [r11 - 192], r12
	mov r12d, 0
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10 - 104]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 200], r13
	mov r13d, 0
	adox r14, r13
	mov [r11 - 208], r14
	mov r14d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	adox r8, rbx
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 24]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 16]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10]
	adox r15, rcx
	adcx r8, rax
	mov [rsp + 48], r15
	mov r15d, 0
	mulx rax, rcx, [r10 - 8]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	adox r14, r15
	mov [rsp + 56], r8
	mov [rsp + 64], rbx
	mov [rsp + 72], rsi
	mov [rsp + 80], rdi
	mov [rsp + 88], r12
	mov [rsp + 96], r13
	mov [rsp + 104], r14
	mov rax, [rsp]
	add rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 144]
	mov [r11 - 144], rax
	mov rax, [rsp + 96]
	adc rax, [r11 - 152]
	mov [r11 - 152], rax
	mov rax, [rsp + 104]
	adc rax, [r11 - 160]
	mov [r11 - 160], rax
	adc qword [r11 - 168], 0
	adc qword [r11 - 176], 0
	adc qword [r11 - 184], 0
	adc qword [r11 - 192], 0
	adc qword [r11 - 200], 0
	adc qword [r11 - 208], 0
	adc qword [r11 - 216], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	mov rdx, [r10 - 96]
	mulx r9, r8, rdx
	mov rax, [r11 - 192]
	mov rcx, [r11 - 200]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 192], rax
	mov [r11 - 200], rcx
	mov rdx, [r10 - 104]
	mulx r9, r8, rdx
	mov rax, [r11 - 208]
	mov rcx, [r11 - 216]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 208], rax
	mov [r11 - 216], rcx
	add rsp, 136
	jmp .done
.n15:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	mov [r11], rbx
	mov [r11 - 104], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 56], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	adox r12, rbx
	mov [r11 - 64], rsi
	mov esi, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 72], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, rdi
	mov [r11 - 80], r12
	mov r12d, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 88], r13
	mov r13d, 0
	adox r14, r13
	mov [r11 - 96], r14
	mov r14d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov [r11 - 112], rbx
	mov [r11 - 232], rbx
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10 - 64]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 120], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 72]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 80]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 88]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 96]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 104]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 112]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 128], rdi
	mov edi, 0
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 136], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 80]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 96]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 104]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 112]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 144], r13
	mov r13d, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 152], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 88]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 96]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 104]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 112]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 160], r15
	mov r15d, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox r8, rcx
	adcx rbx, rax
	mov [r11 - 168], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 96]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 104]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 112]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 176], rbx
	mov ebx, 0
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10 - 96]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 184], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 104]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 112]
	adox r12, rcx
	adcx r13, rax
	adox r13, rsi
	mov [r11 - 192], rdi
	mov edi, 0
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10 - 104]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 200], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 112]
	adox r13, rcx
	adcx r14, rax
	adox r14, r12
	mov [r11 - 208], r13
	mov r13d, 0
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10 - 112]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 216], r14
	mov r14d, 0
	adox r15, r14
	mov [r11 - 224], r15
	mov r15d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov rdx, [r10 - 56]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	adox r8, rbx
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 24]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 16]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10]
	adox r15, rcx
	adcx r8, rax
	mov [rsp + 48], r15
	mov r15d, 0
	mulx rax, rcx, [r10 - 8]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	adox r14, r15
	mov rdx, [r10 - 112]
	mulx rax, rcx, [r10]
	adox r8, rcx
	adcx rbx, rax
	mov [rsp + 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 8]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 16]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 24]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 32]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 40]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 48]
	adox r14, rcx
	adcx r15, rax
	adox r15, r8
	mov [rsp + 64], rbx
	mov [rsp + 72], rsi
	mov [rsp + 80], rdi
	mov [rsp + 88], r12
	mov [rsp + 96], r13
	mov [rsp + 104], r14
	mov [rsp + 112], r15
	mov rax, [rsp]
	add rax, [r11 - 56]
	mov [r11 - 56], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 144]
	mov [r11 - 144], rax
	mov rax, [rsp + 96]
	adc rax, [r11 - 152]
	mov [r11 - 152], rax
	mov rax, [rsp + 104]
	adc rax, [r11 - 160]
	mov [r11 - 160], rax
	mov rax, [rsp + 112]
	adc rax, [r11 - 168]
	mov [r11 - 168], rax
	adc qword [r11 - 176], 0
	adc qword [r11 - 184], 0
	adc qword [r11 - 192], 0
	adc qword [r11 - 200], 0
	adc qword [r11 - 208], 0
	adc qword [r11 - 216], 0
	adc qword [r11 - 224], 0
	adc qword [r11 - 232], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	mov rdx, [r10 - 96]
	mulx r9, r8, rdx
	mov rax, [r11 - 192]
	mov rcx, [r11 - 200]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 192], rax
	mov [r11 - 200], rcx
	mov rdx, [r10 - 104]
	mulx r9, r8, rdx
	mov rax, [r11 - 208]
	mov rcx, [r11 - 216]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 208], rax
	mov [r11 - 216], rcx
	mov rdx, [r10 - 112]
	mulx r9, r8, rdx
	mov rax, [r11 - 224]
	mov rcx, [r11 - 232]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 224], rax
	mov [r11 - 232], rcx
	add rsp, 136
	jmp .done
.n16:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov [r11], rbx
	mov [r11 - 120], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx rbx, rax
	mov [r11 - 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 64], rbx
	mov ebx, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 72], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	adox r13, rsi
	mov [r11 - 80], rdi
	mov edi, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 88], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	adox r14, r12
	mov [r11 - 96], r13
	mov r13d, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 104], r14
	mov r14d, 0
	adox r15, r14
	mov [r11 - 112], r15
	mov r15d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov [r11 - 128], rbx
	mov [r11 - 248], rbx
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 136], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 80]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 88]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 96]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 104]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 112]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 120]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 144], rdi
	mov edi, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 152], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 88]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 96]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 104]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 112]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 120]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 160], r13
	mov r13d, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 168], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 96]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 104]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 112]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 120]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 176], r15
	mov r15d, 0
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10 - 96]
	adox r8, rcx
	adcx rbx, rax
	mov [r11 - 184], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 104]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 112]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 120]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 192], rbx
	mov ebx, 0
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10 - 104]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 200], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 112]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 120]
	adox r12, rcx
	adcx r13, rax
	adox r13, rsi
	mov [r11 - 208], rdi
	mov edi, 0
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10 - 112]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 216], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 120]
	adox r13, rcx
	adcx r14, rax
	adox r14, r12
	mov [r11 - 224], r13
	mov r13d, 0
	mov rdx, [r10 - 112]
	mulx rax, rcx, [r10 - 120]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 232], r14
	mov r14d, 0
	adox r15, r14
	mov [r11 - 240], r15
	mov r15d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	xor r9d, r9d
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx r9, rax
	adox r9, rbx
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 56]
	adox r9, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 48]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 40]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 24]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 32]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 24]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov rdx, [r10 - 112]
	mulx rax, rcx, [r10]
	adox r15, rcx
	adcx r8, rax
	mov [rsp + 48], r15
	mov r15d, 0
	mulx rax, rcx, [r10 - 8]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 16]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	adox r14, r15
	mov rdx, [r10 - 120]
	mulx rax, rcx, [r10]
	adox r8, rcx
	adcx r9, rax
	mov [rsp + 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 8]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 56]
	adox r14, rcx
	adcx r15, rax
	adox r15, r8
	mov [rsp + 64], r9
	mov [rsp + 72], rbx
	mov [rsp + 80], rsi
	mov [rsp + 88], rdi
	mov [rsp + 96], r12
	mov [rsp + 104], r13
	mov [rsp + 112], r14
	mov [rsp + 120], r15
	mov rax, [rsp]
	add rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 144]
	mov [r11 - 144], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 152]
	mov [r11 - 152], rax
	mov rax, [rsp + 96]
	adc rax, [r11 - 160]
	mov [r11 - 160], rax
	mov rax, [rsp + 104]
	adc rax, [r11 - 168]
	mov [r11 - 168], rax
	mov rax, [rsp + 112]
	adc rax, [r11 - 176]
	mov [r11 - 176], rax
	mov rax, [rsp + 120]
	adc rax, [r11 - 184]
	mov [r11 - 184], rax
	adc qword [r11 - 192], 0
	adc qword [r11 - 200], 0
	adc qword [r11 - 208], 0
	adc qword [r11 - 216], 0
	adc qword [r11 - 224], 0
	adc qword [r11 - 232], 0
	adc qword [r11 - 240], 0
	adc qword [r11 - 248], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	mov rdx, [r10 - 96]
	mulx r9, r8, rdx
	mov rax, [r11 - 192]
	mov rcx, [r11 - 200]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 192], rax
	mov [r11 - 200], rcx
	mov rdx, [r10 - 104]
	mulx r9, r8, rdx
	mov rax, [r11 - 208]
	mov rcx, [r11 - 216]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 208], rax
	mov [r11 - 216], rcx
	mov rdx, [r10 - 112]
	mulx r9, r8, rdx
	mov rax, [r11 - 224]
	mov rcx, [r11 - 232]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 224], rax
	mov [r11 - 232], rcx
	mov rdx, [r10 - 120]
	mulx r9, r8, rdx
	mov rax, [r11 - 240]
	mov rcx, [r11 - 248]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 240], rax
	mov [r11 - 248], rcx
	add rsp, 136
	jmp .done
.n17:
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	mov [r11], rbx
	mov [r11 - 120], rbx
	mov rdx, [r10]
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 16], rdi
	mov edi, 0
	mov rdx, [r10 - 8]
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 32], r13
	mov r13d, 0
	mov rdx, [r10 - 16]
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 48], r15
	mov r15d, 0
	mov rdx, [r10 - 24]
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx rbx, rax
	mov [r11 - 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 64], rbx
	mov ebx, 0
	mov rdx, [r10 - 32]
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 72], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	adox r13, rsi
	mov [r11 - 80], rdi
	mov edi, 0
	mov rdx, [r10 - 40]
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 88], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	adox r14, r12
	mov [r11 - 96], r13
	mov r13d, 0
	mov rdx, [r10 - 48]
	mulx rax, rcx, [r10 - 56]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 104], r14
	mov r14d, 0
	adox r15, r14
	mov [r11 - 112], r15
	mov r15d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	xor r9d, r9d
	mov [r11 - 128], rbx
	mov [r11 - 264], rbx
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10 - 72]
	adox rsi, rcx
	adcx rdi, rax
	mov [r11 - 136], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 80]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 88]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 96]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 104]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 112]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 120]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 128]
	adox r9, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov [r11 - 144], rdi
	mov edi, 0
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10 - 80]
	adox r12, rcx
	adcx r13, rax
	mov [r11 - 152], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 88]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 96]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 104]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 112]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 120]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 128]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, r12
	mov [r11 - 160], r13
	mov r13d, 0
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10 - 88]
	adox r14, rcx
	adcx r15, rax
	mov [r11 - 168], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 96]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 104]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 112]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 120]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 128]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r14
	mov [r11 - 176], r15
	mov r15d, 0
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10 - 96]
	adox r8, rcx
	adcx r9, rax
	mov [r11 - 184], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 104]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 112]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 120]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 128]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r8
	mov [r11 - 192], r9
	mov r9d, 0
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10 - 104]
	adox rbx, rcx
	adcx rsi, rax
	mov [r11 - 200], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 112]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 120]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 128]
	adox r12, rcx
	adcx r13, rax
	adox r13, rbx
	mov [r11 - 208], rsi
	mov esi, 0
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10 - 112]
	adox rdi, rcx
	adcx r12, rax
	mov [r11 - 216], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 120]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 128]
	adox r13, rcx
	adcx r14, rax
	adox r14, rdi
	mov [r11 - 224], r12
	mov r12d, 0
	mov rdx, [r10 - 112]
	mulx rax, rcx, [r10 - 120]
	adox r13, rcx
	adcx r14, rax
	mov [r11 - 232], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 128]
	adox r14, rcx
	adcx r15, rax
	adox r15, r13
	mov [r11 - 240], r14
	mov r14d, 0
	mov rdx, [r10 - 120]
	mulx rax, rcx, [r10 - 128]
	adox r15, rcx
	adcx r8, rax
	mov [r11 - 248], r15
	mov r15d, 0
	adox r8, r15
	mov [r11 - 256], r8
	mov r8d, 0
	xor ebx, ebx
	xor esi, esi
	xor edi, edi
	xor r12d, r12d
	xor r13d, r13d
	xor r14d, r14d
	xor r15d, r15d
	xor r8d, r8d
	xor r9d, r9d
	mov rdx, [r10 - 64]
	mulx rax, rcx, [r10]
	adox rbx, rcx
	adcx rsi, rax
	mov [rsp], rbx
	mov ebx, 0
	mulx rax, rcx, [r10 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 16]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 32]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 40]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 48]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 56]
	adox r8, rcx
	adcx r9, rax
	adox r9, rbx
	mov rdx, [r10 - 72]
	mulx rax, rcx, [r10]
	adox rsi, rcx
	adcx rdi, rax
	mov [rsp + 8], rsi
	mov esi, 0
	mulx rax, rcx, [r10 - 8]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 24]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 32]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 40]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 48]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 56]
	adox r9, rcx
	adcx rbx, rax
	adox rbx, rsi
	mov rdx, [r10 - 80]
	mulx rax, rcx, [r10]
	adox rdi, rcx
	adcx r12, rax
	mov [rsp + 16], rdi
	mov edi, 0
	mulx rax, rcx, [r10 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 16]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 24]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 32]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 40]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 48]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 56]
	adox rbx, rcx
	adcx rsi, rax
	adox rsi, rdi
	mov rdx, [r10 - 88]
	mulx rax, rcx, [r10]
	adox r12, rcx
	adcx r13, rax
	mov [rsp + 24], r12
	mov r12d, 0
	mulx rax, rcx, [r10 - 8]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 16]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 24]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 32]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 40]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 48]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 56]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r12
	mov rdx, [r10 - 96]
	mulx rax, rcx, [r10]
	adox r13, rcx
	adcx r14, rax
	mov [rsp + 32], r13
	mov r13d, 0
	mulx rax, rcx, [r10 - 8]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 16]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 24]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 32]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 40]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 48]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 56]
	adox rdi, rcx
	adcx r12, rax
	adox r12, r13
	mov rdx, [r10 - 104]
	mulx rax, rcx, [r10]
	adox r14, rcx
	adcx r15, rax
	mov [rsp + 40], r14
	mov r14d, 0
	mulx rax, rcx, [r10 - 8]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r10 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 24]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 32]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 48]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 56]
	adox r12, rcx
	adcx r13, rax
	adox r13, r14
	mov rdx, [r10 - 112]
	mulx rax, rcx, [r10]
	adox r15, rcx
	adcx r8, rax
	mov [rsp + 48], r15
	mov r15d, 0
	mulx rax, rcx, [r10 - 8]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r10 - 16]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 24]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 40]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 48]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 56]
	adox r13, rcx
	adcx r14, rax
	adox r14, r15
	mov rdx, [r10 - 120]
	mulx rax, rcx, [r10]
	adox r8, rcx
	adcx r9, rax
	mov [rsp + 56], r8
	mov r8d, 0
	mulx rax, rcx, [r10 - 8]
	adox r9, rcx
	adcx rbx, rax
	mulx rax, rcx, [r10 - 16]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 32]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 40]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 48]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 56]
	adox r14, rcx
	adcx r15, rax
	adox r15, r8
	mov rdx, [r10 - 128]
	mulx rax, rcx, [r10]
	adox r9, rcx
	adcx rbx, rax
	mov [rsp + 64], r9
	mov r9d, 0
	mulx rax, rcx, [r10 - 8]
	adox rbx, rcx
	adcx rsi, rax
	mulx rax, rcx, [r10 - 16]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r10 - 24]
	adox rdi, rcx
	adcx r12, rax
	mulx rax, rcx, [r10 - 32]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r10 - 40]
	adox r13, rcx
	adcx r14, rax
	mulx rax, rcx, [r10 - 48]
	adox r14, rcx
	adcx r15, rax
	mulx rax, rcx, [r10 - 56]
	adox r15, rcx
	adcx r8, rax
	adox r8, r9
	mov [rsp + 72], rbx
	mov [rsp + 80], rsi
	mov [rsp + 88], rdi
	mov [rsp + 96], r12
	mov [rsp + 104], r13
	mov [rsp + 112], r14
	mov [rsp + 120], r15
	mov [rsp + 128], r8
	mov rax, [rsp]
	add rax, [r11 - 64]
	mov [r11 - 64], rax
	mov rax, [rsp + 8]
	adc rax, [r11 - 72]
	mov [r11 - 72], rax
	mov rax, [rsp + 16]
	adc rax, [r11 - 80]
	mov [r11 - 80], rax
	mov rax, [rsp + 24]
	adc rax, [r11 - 88]
	mov [r11 - 88], rax
	mov rax, [rsp + 32]
	adc rax, [r11 - 96]
	mov [r11 - 96], rax
	mov rax, [rsp + 40]
	adc rax, [r11 - 104]
	mov [r11 - 104], rax
	mov rax, [rsp + 48]
	adc rax, [r11 - 112]
	mov [r11 - 112], rax
	mov rax, [rsp + 56]
	adc rax, [r11 - 120]
	mov [r11 - 120], rax
	mov rax, [rsp + 64]
	adc rax, [r11 - 128]
	mov [r11 - 128], rax
	mov rax, [rsp + 72]
	adc rax, [r11 - 136]
	mov [r11 - 136], rax
	mov rax, [rsp + 80]
	adc rax, [r11 - 144]
	mov [r11 - 144], rax
	mov rax, [rsp + 88]
	adc rax, [r11 - 152]
	mov [r11 - 152], rax
	mov rax, [rsp + 96]
	adc rax, [r11 - 160]
	mov [r11 - 160], rax
	mov rax, [rsp + 104]
	adc rax, [r11 - 168]
	mov [r11 - 168], rax
	mov rax, [rsp + 112]
	adc rax, [r11 - 176]
	mov [r11 - 176], rax
	mov rax, [rsp + 120]
	adc rax, [r11 - 184]
	mov [r11 - 184], rax
	mov rax, [rsp + 128]
	adc rax, [r11 - 192]
	mov [r11 - 192], rax
	adc qword [r11 - 200], 0
	adc qword [r11 - 208], 0
	adc qword [r11 - 216], 0
	adc qword [r11 - 224], 0
	adc qword [r11 - 232], 0
	adc qword [r11 - 240], 0
	adc qword [r11 - 248], 0
	adc qword [r11 - 256], 0
	adc qword [r11 - 264], 0
	xor eax, eax
	mov rdx, [r10]
	mulx r9, r8, rdx
	mov rax, [r11]
	mov rcx, [r11 - 8]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11], rax
	mov [r11 - 8], rcx
	mov rdx, [r10 - 8]
	mulx r9, r8, rdx
	mov rax, [r11 - 16]
	mov rcx, [r11 - 24]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 16], rax
	mov [r11 - 24], rcx
	mov rdx, [r10 - 16]
	mulx r9, r8, rdx
	mov rax, [r11 - 32]
	mov rcx, [r11 - 40]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 32], rax
	mov [r11 - 40], rcx
	mov rdx, [r10 - 24]
	mulx r9, r8, rdx
	mov rax, [r11 - 48]
	mov rcx, [r11 - 56]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 48], rax
	mov [r11 - 56], rcx
	mov rdx, [r10 - 32]
	mulx r9, r8, rdx
	mov rax, [r11 - 64]
	mov rcx, [r11 - 72]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 64], rax
	mov [r11 - 72], rcx
	mov rdx, [r10 - 40]
	mulx r9, r8, rdx
	mov rax, [r11 - 80]
	mov rcx, [r11 - 88]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 80], rax
	mov [r11 - 88], rcx
	mov rdx, [r10 - 48]
	mulx r9, r8, rdx
	mov rax, [r11 - 96]
	mov rcx, [r11 - 104]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 96], rax
	mov [r11 - 104], rcx
	mov rdx, [r10 - 56]
	mulx r9, r8, rdx
	mov rax, [r11 - 112]
	mov rcx, [r11 - 120]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 112], rax
	mov [r11 - 120], rcx
	mov rdx, [r10 - 64]
	mulx r9, r8, rdx
	mov rax, [r11 - 128]
	mov rcx, [r11 - 136]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 128], rax
	mov [r11 - 136], rcx
	mov rdx, [r10 - 72]
	mulx r9, r8, rdx
	mov rax, [r11 - 144]
	mov rcx, [r11 - 152]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 144], rax
	mov [r11 - 152], rcx
	mov rdx, [r10 - 80]
	mulx r9, r8, rdx
	mov rax, [r11 - 160]
	mov rcx, [r11 - 168]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 160], rax
	mov [r11 - 168], rcx
	mov rdx, [r10 - 88]
	mulx r9, r8, rdx
	mov rax, [r11 - 176]
	mov rcx, [r11 - 184]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 176], rax
	mov [r11 - 184], rcx
	mov rdx, [r10 - 96]
	mulx r9, r8, rdx
	mov rax, [r11 - 192]
	mov rcx, [r11 - 200]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 192], rax
	mov [r11 - 200], rcx
	mov rdx, [r10 - 104]
	mulx r9, r8, rdx
	mov rax, [r11 - 208]
	mov rcx, [r11 - 216]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 208], rax
	mov [r11 - 216], rcx
	mov rdx, [r10 - 112]
	mulx r9, r8, rdx
	mov rax, [r11 - 224]
	mov rcx, [r11 - 232]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 224], rax
	mov [r11 - 232], rcx
	mov rdx, [r10 - 120]
	mulx r9, r8, rdx
	mov rax, [r11 - 240]
	mov rcx, [r11 - 248]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 240], rax
	mov [r11 - 248], rcx
	mov rdx, [r10 - 128]
	mulx r9, r8, rdx
	mov rax, [r11 - 256]
	mov rcx, [r11 - 264]
	adcx rax, rax
	adcx rcx, rcx
	adox rax, r8
	adox rcx, r9
	mov [r11 - 256], rax
	mov [r11 - 264], rcx
	add rsp, 136
	jmp .done
align 8
.midTbl: dq .n10 - .midTbl, .n11 - .midTbl, .n12 - .midTbl, .n13 - .midTbl, .n14 - .midTbl, .n15 - .midTbl, .n16 - .midTbl, .n17 - .midTbl
align 8
.smallTbl: dq .s1 - .smallTbl, .s2 - .smallTbl, .s3 - .smallTbl, .s4 - .smallTbl, .s5 - .smallTbl, .s6 - .smallTbl, .s7 - .smallTbl, .s8 - .smallTbl, .s9 - .smallTbl
