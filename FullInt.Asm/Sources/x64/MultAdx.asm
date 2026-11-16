
default rel
section .text

; =============================================================================
;  R = A * B with mulx / adcx / adox (BMI2 + ADX: Broadwell, Zen and later).
;  A new kernel next to Mult.asm (the baseline, kept as is); picked at start-up when
;  Fullint64CpuFeatures reports BMI2 and ADX.
;
;  Limb order: index 0 = most significant word (as every kernel here).
;  Contract as Fullint64MultWIN: lenR >= lenA + lenB; words of R above lenA + lenB are zero.
;
;  Windows : void Fullint64MultWIN_adx(A, B, R, int lenA, int lenB, int lenR)
;  System V: void Fullint64Mult_adx  (A, B, R, int lenA, int lenB, int lenR)   (the same C prototype)
;
;  Method: rows. Row 0 stores A * b_0; then for each further word b_j of the shorter operand (from the low
;  end) R[j ..] += A * b_j:
;    mulx  hi, lo, a_i          ; rdx = b_j; flags untouched
;    adcx  lo, prev_hi          ; carry chain 1 (CF): the high word of the previous product
;    adox  lo, r_(i+j)          ; carry chain 2 (OF): the word already in R
;  The two chains do not wait for each other. Loop control uses lea / jrcxz only (no flags).
;  The row's top word r_(lenA+j) is new: prev_hi + CF + OF, stored.
;
;  Registers (System V body):
;    r14  a_0 (A's lowest word, the end of A)      rsi  a_i walking up the number (down in memory)
;    rbx  b_j                                      rdi  r_(i+j)
;    r10  r_j (the row's lowest R word)            rcx  words left in the row
;    r11  lenA (the longer)                        rdx  b_j for mulx
;    r13  lenA mod 8 (single words of a row)       r15  lenA / 8 (groups of eight)
;    r12  rows left (lenB, the shorter)            r8   prev_hi    r9  hi    rax  lo
; =============================================================================

global Fullint64MultWIN_adx
Fullint64MultWIN_adx:
	push rdi                    ; Win64 non-volatile, used by the body
	push rsi
	mov rdi, rcx                ; A
	mov rsi, rdx                ; B
	mov rdx, r8                 ; R
	mov ecx, r9d                ; lenA
	mov r8d, dword [rsp+38h]    ; lenB (28h + 2 pushes)
	mov r9d, dword [rsp+40h]    ; lenR (30h + 2 pushes)
	call Fullint64Mult_adx
	pop rsi
	pop rdi
	ret

global Fullint64Mult_adx
Fullint64Mult_adx:
	push rbx                    ; non-volatile in both ABIs
	push r12
	push r13
	push r14
	push r15

	movsxd r11, ecx             ; lenA
	movsxd r12, r8d             ; lenB
	movsxd rcx, r9d             ; lenR
	mov r10, rdx                ; R

	; the longer operand in the rows, the shorter one counts the rows
	cmp r11, r12
	jae .ordered
	xchg rdi, rsi
	xchg r11, r12
.ordered:
	lea r14, [rdi + r11*8 - 8]  ; a_0
	lea rbx, [rsi + r12*8 - 8]  ; b_0
	mov r13, r11
	and r13, 7                  ; single words first: lenA mod 8
	mov r15, r11
	shr r15, 3                  ; then groups of eight (counted here: in a row the flags are the carries;
	                            ; eight a pass: 0.88-0.90 of four at 24-96 words, Zen 3)

	; words of R above lenA + lenB (a longer R) are zero; no other clearing: row 0 stores
	mov rdi, r10                ; R
	lea r10, [r10 + rcx*8 - 8]  ; r_0 = R's last word
	sub rcx, r11
	sub rcx, r12                ; extra words
	jle .row0
	xor eax, eax
.zero:
	mov [rdi], rax
	add rdi, 8
	dec rcx
	jnz .zero

.row0:                          ; R[0 .. lenA] = A * b_0: stored, nothing to add (one carry chain)
	mov rdx, [rbx]
	mov rsi, r14
	mov rdi, r10
	mov rcx, r13
	xor r8d, r8d                ; clears CF
.word0:
	jrcxz .quads0
	mulx r9, rax, [rsi]
	adcx rax, r8
	mov [rdi], rax
	mov r8, r9
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	lea rcx, [rcx - 1]
	jmp .word0
.quads0:
	mov rcx, r15
.quad0:
	jrcxz .quad0Out
	jmp .quad0Body
.quad0Out:
	jmp .row0End
.quad0Body:
	mulx r9, rax, [rsi]
	adcx rax, r8
	mov [rdi], rax
	mulx r8, rax, [rsi - 8]
	adcx rax, r9
	mov [rdi - 8], rax
	mulx r9, rax, [rsi - 16]
	adcx rax, r8
	mov [rdi - 16], rax
	mulx r8, rax, [rsi - 24]
	adcx rax, r9
	mov [rdi - 24], rax
	mulx r9, rax, [rsi - 32]
	adcx rax, r8
	mov [rdi - 32], rax
	mulx r8, rax, [rsi - 40]
	adcx rax, r9
	mov [rdi - 40], rax
	mulx r9, rax, [rsi - 48]
	adcx rax, r8
	mov [rdi - 48], rax
	mulx r8, rax, [rsi - 56]
	adcx rax, r9
	mov [rdi - 56], rax
	lea rsi, [rsi - 64]
	lea rdi, [rdi - 64]
	lea rcx, [rcx - 1]
	jmp .quad0
.row0End:
	mov eax, 0
	adcx r8, rax
	mov [rdi], r8               ; r_lenA
	lea rbx, [rbx - 8]
	lea r10, [r10 - 8]
	dec r12
	jz .done

.row:
	mov rdx, [rbx]              ; b_j
	mov rsi, r14
	mov rdi, r10
	mov rcx, r13
	xor r8d, r8d                ; prev_hi = 0; clears CF and OF
.word:
	jrcxz .quads
	mulx r9, rax, [rsi]         ; r9:rax = a_i * b_j
	adcx rax, r8                ; + prev_hi + CF
	adox rax, [rdi]             ; + r_(i+j) + OF
	mov [rdi], rax
	mov r8, r9
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	lea rcx, [rcx - 1]
	jmp .word
.quads:
	mov rcx, r15
.quad:
	jrcxz .quadOut
	jmp .quadBody
.quadOut:
	jmp .rowEnd
.quadBody:
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
	adox r8, rax                ; + OF: no carry out of the row's top word
	mov [rdi], r8               ; r_(lenA+j): not written by earlier rows

	lea rbx, [rbx - 8]          ; b_(j+1)
	lea r10, [r10 - 8]          ; r_(j+1)
	dec r12
	jnz .row

.done:
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	ret
