default rel
%include "abi.inc"
section .text

; =============================================================================
;  The low half of a product: R = A·B mod β^n, with mulx / adcx / adox (BMI2 + ADX), next to
;  MultAdx.asm. For the divisions through a reciprocal (a remainder X - q·B is known to fit a
;  few words, so only the low words of q·B matter) and the short products built on it.
;
;  Windows: void Fullint64MulLoWIN_adx(A, B, R, int lenA, int lenB, int n)
;    1 <= n <= lenA + lenB; R: n words. Limb order: index 0 = most significant word.
;
;  Rows as MultAdx (R cleared first): row j (b_j from the low end) adds A·b_j at r_j, but only
;  its words below position n: min(lenA, n - j) of them; the row's top word r_(lenA+j) when it
;  is below n, otherwise its carry is dropped. Rows j < min(lenB, n). About half the products
;  of the full multiply when lenA = lenB = n.
;    mulx  hi, lo, a_i          ; rdx = b_j
;    adcx  lo, prev_hi          ; carry chain 1 (CF)
;    adox  lo, r_(i+j)          ; carry chain 2 (OF)
;  Loop control by lea / jrcxz only.
;
;  Registers: r14 a_0 (A's lowest word), rbx b_j, r10 r_j, r12 rows left, r11 lenA, rbp n - j
;  (the room of the row), r13 1 when the row's top word fits, rsi a_i, rdi r_(i+j), rcx words
;  left in the row, r15 the row's groups of four, r8 prev_hi, r9 hi, rax lo, rdx b_j for mulx.
; =============================================================================
ENTRY Fullint64MulLoWIN_adx
	WIN64_ARGS
	push rbx
	push rbp
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	movsxd r11, r9d             ; lenA
	movsxd r12, dword [rsp + 104]   ; lenB (5th argument: rsp + 40 at entry, 8 pushes)
	movsxd rbp, dword [rsp + 112]   ; n (6th)
	mov rsi, rcx                ; A
	mov r13, rdx                ; B
	mov r10, r8                 ; R

	; R = 0 (n words)
	mov rdi, r10
	mov rcx, rbp
	xor eax, eax
	rep stosq

	lea r14, [rsi + r11*8 - 8]  ; a_0
	lea rbx, [r13 + r12*8 - 8]  ; b_0
	lea r10, [r10 + rbp*8 - 8]  ; r_0
	cmp r12, rbp
	cmova r12, rbp              ; rows: min(lenB, n)

.row:
	mov rdx, [rbx]              ; b_j
	test rdx, rdx
	jz .rowNext                 ; b_j = 0: nothing
	xor r13d, r13d
	cmp rbp, r11
	seta r13b                   ; 1: room > lenA, the row's top word r_(lenA+j) is below n
	mov rcx, rbp                ; the room: n - j words
	cmp rcx, r11
	cmova rcx, r11              ; the row's words: min(lenA, n - j)
	mov r15, rcx
	shr r15, 2                  ; groups of four
	and rcx, 3                  ; single words first
	mov rsi, r14
	mov rdi, r10
	xor r8d, r8d                ; prev_hi = 0; CF = OF = 0
.word:
	jrcxz .quads
	mulx r9, rax, [rsi]
	adcx rax, r8
	adox rax, [rdi]
	mov [rdi], rax
	mov r8, r9
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	lea rcx, [rcx - 1]
	jmp .word
.quads:
	mov rcx, r15
.quad:
	jrcxz .rowEnd
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
	lea rsi, [rsi - 32]
	lea rdi, [rdi - 32]
	lea rcx, [rcx - 1]
	jmp .quad
.rowEnd:
	mov rcx, r13                ; (decided before the row: the flags are its carries now)
	jrcxz .rowNext              ; the row ended at n: its carry is dropped
	mov eax, 0
	adcx r8, rax
	adox r8, rax
	mov [rdi], r8               ; not written by earlier rows
.rowNext:
	lea rbx, [rbx - 8]          ; b_(j+1)
	lea r10, [r10 - 8]          ; r_(j+1)
	dec rbp                     ; the room shrinks
	dec r12
	jnz .row

	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbp
	pop rbx
	WRET
