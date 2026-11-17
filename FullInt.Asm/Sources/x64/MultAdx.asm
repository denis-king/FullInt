
default rel
%include "abi.inc"
section .text

; =============================================================================
;  R = A * B with mulx / adcx / adox (BMI2 + ADX: Broadwell, Zen and later).
;  A new kernel next to Mult.asm (the baseline, kept as is); picked at start-up when
;  Fullint64CpuFeatures reports BMI2 and ADX.
;
;  Limb order: index 0 = most significant word (as every kernel here).
;  Contract as Fullint64MultWIN: lenR >= lenA + lenB; words of R above lenA + lenB are zero.
;
;  void Fullint64MultWIN_adx(A, B, R, int lenA, int lenB, int lenR)   (Windows and System V: one
;  entry, abi.inc)
;
;  Method: rows. Row 0 stores A * b_0; then for each further word b_j of the shorter operand (from the low
;  end) R[j ..] += A * b_j:
;    mulx  hi, lo, a_i          ; rdx = b_j; flags untouched
;    adcx  lo, prev_hi          ; carry chain 1 (CF): the high word of the previous product
;    adox  lo, r_(i+j)          ; carry chain 2 (OF): the word already in R
;  The two chains do not wait for each other. Loop control uses lea / jrcxz only (no flags).
;  The row's top word r_(lenA+j) is new: prev_hi + CF + OF, stored.
;
;  lenA < 8 (.small): the lenA + 1 words a row adds into stay in registers, rows unrolled for each lenA,
;  no R word read back (the rows above chain through memory: a stored word is the next row's load,
;  store forwarding ~ 6 cycles a row). Zen 3, n x n: 2 words 4.7 ns (mpn_mul_n 5.7, the rows 6.8),
;  4: 8.5 (9.9, 15), 7: 21 (28, 35).
;
;  Registers (System V body):
;    r14  a_0 (A's lowest word, the end of A)      rsi  a_i walking up the number (down in memory)
;    rbx  b_j                                      rdi  r_(i+j)
;    r10  r_j (the row's lowest R word)            rcx  words left in the row
;    r11  lenA (the longer)                        rdx  b_j for mulx
;    r13  lenA mod 8 (single words of a row)       r15  lenA / 8 (groups of eight)
;    r12  rows left (lenB, the shorter)            r8   prev_hi    r9  hi    rax  lo
; =============================================================================


ENTRY Fullint64MultWIN_adx
	SAVE rdi, rsi
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rdx, r8
	mov ecx, r9d
	mov r8d, dword STACKARG(5, FI_NPUSH)
	mov r9d, dword STACKARG(6, FI_NPUSH)
%else
	mov ecx, ecx
	mov r8d, r8d
	mov r9d, r9d
%endif
Fullint64Mult_adx_body:
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
	jle .rows
	xor eax, eax
.zero:
	mov [rdi], rax
	add rdi, 8
	dec rcx
	jnz .zero

.rows:
	test r15, r15
	jz .small
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
	ARET rdi, rsi
	; lenA < 8 (lenB <= lenA): the lenA + 1 words of R being added to stay in registers. Row j adds A * b_j:
	;    mulx hi, lo, a_k ; adox w_(j+k), lo (chain O) ; adcx w_(j+k+1), hi (chain C)
	; w_j is final after its first block: stored, then zeroed (mov: the flags stay) - the zero for the row's
	; last OF, and the top word of the next row. A row ends with CF = OF = 0. The window rotates through
	; W[m mod (lenA + 1)], rows unrolled for each lenA; entered at row s = lenA - lenB with b_0 and r_0 biased
	; by 8s, so every lenA has one ending (the last lenA words stored). No R word is read back.
	; Registers: rax hi, rcx lo, rdx b_j, r14 a_0, rbx b_0 + 8s, r10 r_0 + 8s, the window r8 r9 r11 r12 r13 r15 rsi rdi
.small:
	lea rax, [r11 + r11*8]
	sub rax, r12                ; 9 lenA - lenB = 8 lenA + s
	lea rcx, [rel .smallTbl]
	mov rax, [rcx + rax*8 - 64]
	add rax, rcx
	mov rcx, r11
	sub rcx, r12
	shl ecx, 3                  ; 8s
	add rbx, rcx
	add r10, rcx
	xor r8d, r8d
	xor r9d, r9d
	xor r11d, r11d
	xor r12d, r12d
	xor r13d, r13d
	xor r15d, r15d
	xor esi, esi
	xor edi, edi
	jmp rax
.m1_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	adox r9, r8
	mov [r10 - 8], r9
	jmp .done
.m2_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	adox r11, r8
.m2_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r8, rax
	adox r8, r9
	mov [r10 - 16], r11
	mov [r10 - 24], r8
	jmp .done
.m3_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 16]
	adox r11, rcx
	adcx r12, rax
	adox r12, r8
.m3_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 16]
	adox r12, rcx
	adcx r8, rax
	adox r8, r9
.m3_2:
	mov rdx, [rbx - 16]
	mulx rax, rcx, [r14]
	adox r11, rcx
	adcx r12, rax
	mov [r10 - 16], r11
	mov r11d, 0
	mulx rax, rcx, [r14 - 8]
	adox r12, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 16]
	adox r8, rcx
	adcx r9, rax
	adox r9, r11
	mov [r10 - 24], r12
	mov [r10 - 32], r8
	mov [r10 - 40], r9
	jmp .done
.m4_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 16]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 24]
	adox r12, rcx
	adcx r13, rax
	adox r13, r8
.m4_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 24]
	adox r13, rcx
	adcx r8, rax
	adox r8, r9
.m4_2:
	mov rdx, [rbx - 16]
	mulx rax, rcx, [r14]
	adox r11, rcx
	adcx r12, rax
	mov [r10 - 16], r11
	mov r11d, 0
	mulx rax, rcx, [r14 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 16]
	adox r13, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 24]
	adox r8, rcx
	adcx r9, rax
	adox r9, r11
.m4_3:
	mov rdx, [rbx - 24]
	mulx rax, rcx, [r14]
	adox r12, rcx
	adcx r13, rax
	mov [r10 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r14 - 8]
	adox r13, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 24]
	adox r9, rcx
	adcx r11, rax
	adox r11, r12
	mov [r10 - 32], r13
	mov [r10 - 40], r8
	mov [r10 - 48], r9
	mov [r10 - 56], r11
	jmp .done
.m5_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 16]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 32]
	adox r13, rcx
	adcx r15, rax
	adox r15, r8
.m5_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 24]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 32]
	adox r15, rcx
	adcx r8, rax
	adox r8, r9
.m5_2:
	mov rdx, [rbx - 16]
	mulx rax, rcx, [r14]
	adox r11, rcx
	adcx r12, rax
	mov [r10 - 16], r11
	mov r11d, 0
	mulx rax, rcx, [r14 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 16]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 24]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 32]
	adox r8, rcx
	adcx r9, rax
	adox r9, r11
.m5_3:
	mov rdx, [rbx - 24]
	mulx rax, rcx, [r14]
	adox r12, rcx
	adcx r13, rax
	mov [r10 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r14 - 8]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 16]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 24]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 32]
	adox r9, rcx
	adcx r11, rax
	adox r11, r12
.m5_4:
	mov rdx, [rbx - 32]
	mulx rax, rcx, [r14]
	adox r13, rcx
	adcx r15, rax
	mov [r10 - 32], r13
	mov r13d, 0
	mulx rax, rcx, [r14 - 8]
	adox r15, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 24]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 32]
	adox r11, rcx
	adcx r12, rax
	adox r12, r13
	mov [r10 - 40], r15
	mov [r10 - 48], r8
	mov [r10 - 56], r9
	mov [r10 - 64], r11
	mov [r10 - 72], r12
	jmp .done
.m6_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 16]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 32]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 40]
	adox r15, rcx
	adcx rsi, rax
	adox rsi, r8
.m6_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 24]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 32]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 40]
	adox rsi, rcx
	adcx r8, rax
	adox r8, r9
.m6_2:
	mov rdx, [rbx - 16]
	mulx rax, rcx, [r14]
	adox r11, rcx
	adcx r12, rax
	mov [r10 - 16], r11
	mov r11d, 0
	mulx rax, rcx, [r14 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 16]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 24]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 32]
	adox rsi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 40]
	adox r8, rcx
	adcx r9, rax
	adox r9, r11
.m6_3:
	mov rdx, [rbx - 24]
	mulx rax, rcx, [r14]
	adox r12, rcx
	adcx r13, rax
	mov [r10 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r14 - 8]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 16]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 24]
	adox rsi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 32]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 40]
	adox r9, rcx
	adcx r11, rax
	adox r11, r12
.m6_4:
	mov rdx, [rbx - 32]
	mulx rax, rcx, [r14]
	adox r13, rcx
	adcx r15, rax
	mov [r10 - 32], r13
	mov r13d, 0
	mulx rax, rcx, [r14 - 8]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 16]
	adox rsi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 24]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 32]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 40]
	adox r11, rcx
	adcx r12, rax
	adox r12, r13
.m6_5:
	mov rdx, [rbx - 40]
	mulx rax, rcx, [r14]
	adox r15, rcx
	adcx rsi, rax
	mov [r10 - 40], r15
	mov r15d, 0
	mulx rax, rcx, [r14 - 8]
	adox rsi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 24]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 32]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 40]
	adox r12, rcx
	adcx r13, rax
	adox r13, r15
	mov [r10 - 48], rsi
	mov [r10 - 56], r8
	mov [r10 - 64], r9
	mov [r10 - 72], r11
	mov [r10 - 80], r12
	mov [r10 - 88], r13
	jmp .done
.m7_0:
	mov rdx, [rbx]
	mulx rax, rcx, [r14]
	adox r8, rcx
	adcx r9, rax
	mov [r10], r8
	mov r8d, 0
	mulx rax, rcx, [r14 - 8]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 16]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 24]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 32]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 40]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 48]
	adox rsi, rcx
	adcx rdi, rax
	adox rdi, r8
.m7_1:
	mov rdx, [rbx - 8]
	mulx rax, rcx, [r14]
	adox r9, rcx
	adcx r11, rax
	mov [r10 - 8], r9
	mov r9d, 0
	mulx rax, rcx, [r14 - 8]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 16]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 24]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 32]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 40]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r14 - 48]
	adox rdi, rcx
	adcx r8, rax
	adox r8, r9
.m7_2:
	mov rdx, [rbx - 16]
	mulx rax, rcx, [r14]
	adox r11, rcx
	adcx r12, rax
	mov [r10 - 16], r11
	mov r11d, 0
	mulx rax, rcx, [r14 - 8]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 16]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 24]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 32]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r14 - 40]
	adox rdi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 48]
	adox r8, rcx
	adcx r9, rax
	adox r9, r11
.m7_3:
	mov rdx, [rbx - 24]
	mulx rax, rcx, [r14]
	adox r12, rcx
	adcx r13, rax
	mov [r10 - 24], r12
	mov r12d, 0
	mulx rax, rcx, [r14 - 8]
	adox r13, rcx
	adcx r15, rax
	mulx rax, rcx, [r14 - 16]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 24]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r14 - 32]
	adox rdi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 40]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 48]
	adox r9, rcx
	adcx r11, rax
	adox r11, r12
.m7_4:
	mov rdx, [rbx - 32]
	mulx rax, rcx, [r14]
	adox r13, rcx
	adcx r15, rax
	mov [r10 - 32], r13
	mov r13d, 0
	mulx rax, rcx, [r14 - 8]
	adox r15, rcx
	adcx rsi, rax
	mulx rax, rcx, [r14 - 16]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r14 - 24]
	adox rdi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 32]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 40]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 48]
	adox r11, rcx
	adcx r12, rax
	adox r12, r13
.m7_5:
	mov rdx, [rbx - 40]
	mulx rax, rcx, [r14]
	adox r15, rcx
	adcx rsi, rax
	mov [r10 - 40], r15
	mov r15d, 0
	mulx rax, rcx, [r14 - 8]
	adox rsi, rcx
	adcx rdi, rax
	mulx rax, rcx, [r14 - 16]
	adox rdi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 24]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 32]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 40]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 48]
	adox r12, rcx
	adcx r13, rax
	adox r13, r15
.m7_6:
	mov rdx, [rbx - 48]
	mulx rax, rcx, [r14]
	adox rsi, rcx
	adcx rdi, rax
	mov [r10 - 48], rsi
	mov esi, 0
	mulx rax, rcx, [r14 - 8]
	adox rdi, rcx
	adcx r8, rax
	mulx rax, rcx, [r14 - 16]
	adox r8, rcx
	adcx r9, rax
	mulx rax, rcx, [r14 - 24]
	adox r9, rcx
	adcx r11, rax
	mulx rax, rcx, [r14 - 32]
	adox r11, rcx
	adcx r12, rax
	mulx rax, rcx, [r14 - 40]
	adox r12, rcx
	adcx r13, rax
	mulx rax, rcx, [r14 - 48]
	adox r13, rcx
	adcx r15, rax
	adox r15, rsi
	mov [r10 - 56], rdi
	mov [r10 - 64], r8
	mov [r10 - 72], r9
	mov [r10 - 80], r11
	mov [r10 - 88], r12
	mov [r10 - 96], r13
	mov [r10 - 104], r15
	jmp .done
align 8
.smallTbl:
	dq .m1_0 - .smallTbl, 0, 0, 0, 0, 0, 0, 0
	dq .m2_0 - .smallTbl, .m2_1 - .smallTbl, 0, 0, 0, 0, 0, 0
	dq .m3_0 - .smallTbl, .m3_1 - .smallTbl, .m3_2 - .smallTbl, 0, 0, 0, 0, 0
	dq .m4_0 - .smallTbl, .m4_1 - .smallTbl, .m4_2 - .smallTbl, .m4_3 - .smallTbl, 0, 0, 0, 0
	dq .m5_0 - .smallTbl, .m5_1 - .smallTbl, .m5_2 - .smallTbl, .m5_3 - .smallTbl, .m5_4 - .smallTbl, 0, 0, 0
	dq .m6_0 - .smallTbl, .m6_1 - .smallTbl, .m6_2 - .smallTbl, .m6_3 - .smallTbl, .m6_4 - .smallTbl, .m6_5 - .smallTbl, 0, 0
	dq .m7_0 - .smallTbl, .m7_1 - .smallTbl, .m7_2 - .smallTbl, .m7_3 - .smallTbl, .m7_4 - .smallTbl, .m7_5 - .smallTbl, .m7_6 - .smallTbl, 0
