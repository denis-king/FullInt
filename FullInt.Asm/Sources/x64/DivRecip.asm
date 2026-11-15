
default rel
section .text

; =============================================================================
;  Division with a precomputed reciprocal of the divisor (Moller, Granlund: "Improved division
;  by invariant integers", IEEE Trans. Computers 2011; GMP divides the same way).
;  New kernels next to Div.asm / Div2Long.asm (the baseline, kept as is). Only mul / imul /
;  add / sub / shld - they run on any x86-64 (Core 2 included); picked at start-up unless
;  FULLINT_CPU=baseline.
;
;  The divisor is the same for the whole division, so its reciprocal is computed once (one div
;  instruction); then every quotient word costs a multiplication or two instead of a div
;  (div r64: ~40-90 cycles on Core 2 / Skylake, ~10-18 on Zen 3; mul r64: 3-5).
;
;  Limbs big-endian (index 0 = most significant word), as everywhere in FullInt.
; =============================================================================


; =============================================================================
;  void Fullint64Div2longWIN_recip(A, d, Q, u64* r, int lenA)
;  The contract of Fullint64Div2longWin: d != 0, lenA >= 1; Q = A / d (lenA words), *r = A mod d.
;
;  d is normalized (shifted left by sh until its top bit is set), A is shifted by the same sh on
;  the fly (shld), the remainder shifted back at the end. Per word (2-by-1, Algorithm 4):
;    (q1:q0) = v * u1 + (u1 + 1 : u0)
;    r = u0 - q1 * d;  r > q0: q1 - 1, r + d;  r >= d (rare): q1 + 1, r - d
;  v = floor((b^2 - 1) / d) - b  (b = 2^64): one div.
;
;  Registers: r11 A, rsi d (normalized), rdi v, r8 Q, r9 r, r10 words left, cl sh,
;  rbx remainder (u1), r12 u0, r13 quotient word, rax / rdx scratch.
; =============================================================================

; one quotient word: u1 = rbx (< d), u0 = r12 -> [r8] (r8 advanced), remainder in rbx
%macro STEP_2BY1 0
	mov rax, rdi
	mul rbx                     ; rdx:rax = v * u1
	add rax, r12                ; q0
	adc rdx, rbx                ; q1 = high + u1 + carry
	lea r13, [rdx + 1]          ;      + 1
	mov rdx, r13
	imul rdx, rsi
	mov rbx, r12
	sub rbx, rdx                ; r = u0 - q1 * d   (mod b)
	cmp rax, rbx                ; r > q0: one too many
	sbb rdx, rdx
	add r13, rdx
	and rdx, rsi
	add rbx, rdx
	cmp rbx, rsi
	jb %%stored
	sub rbx, rsi                ; rare: one too few
	inc r13
%%stored:
	mov [r8], r13
	add r8, 8
%endmacro

global Fullint64Div2longWIN_recip
Fullint64Div2longWIN_recip:
	push rbx
	push rsi
	push rdi
	push r12
	push r13
	movsxd r10, dword [rsp+50h] ; lenA (28h + 5 pushes)
	mov r11, rcx                ; A

	bsr rcx, rdx
	xor ecx, 63                 ; sh = 63 - top bit
	shl rdx, cl
	mov rsi, rdx                ; d normalized
	not rdx                     ; b - 1 - d  (< d: no overflow)
	mov rax, -1
	div rsi
	mov rdi, rax                ; v

	xor ebx, ebx
	mov rax, [r11]
	shld rbx, rax, cl           ; the bits shifted out of A[0] start the remainder (< d)
	dec r10
	jz .last
.words:                         ; A[i] with the top bits of A[i+1]
	mov r12, [r11]
	mov rax, [r11 + 8]
	shld r12, rax, cl
	add r11, 8
	STEP_2BY1
	dec r10
	jnz .words
.last:
	mov r12, [r11]
	shl r12, cl
	STEP_2BY1

	shr rbx, cl                 ; the remainder, not normalized
	mov [r9], rbx
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	ret


; =============================================================================
;  void Fullint64DivWIN_recip(A, B, Q, R, int lenA, int lenB, u64* work)
;  lenA >= lenB >= 2, B without leading zero words (the callers send other cases to the
;  baseline kernel). Q: lenA - lenB + 1 words; R: lenB + 2 words as Fullint64DivWin leaves it
;  (R[0] = R[1] = 0, the remainder in R[2 ..]); work: lenA + 1 words of scratch.
;
;  Long division (Knuth D) on U = [0, A] in work, one quotient word per step, the window
;  W = U[j .. j + lenB] moving down by a pointer (no copying):
;    q^ from the top three words of W and the top two of B, both normalized by sh (shld on
;    the fly; B and U stay as they are): 3-by-2 division with the reciprocal v (Algorithm 5);
;    q^ is the exact quotient of those words, the true word is q^ or (rarely) q^ - 1.
;    W -= q^ * B (two words a pass, the carry in a register; q^ = 0 skips it - the first word
;    of a normalized divisor's quotient is mostly 0); negative: q^ - 1, W += B.
;  (n2, n1) == (d1, d0) - the only case the 3-by-2 cannot take - gives q^ = b - 1.
;  v = floor((b^3 - 1) / (d1 b + d0)) - b: the 2-by-1 reciprocal of d1 (one div), adjusted
;  for d0 (GMP's invert_pi1).
;
;  Registers: rsi B, rdi W (top word), r8 Q, r9 lenB, r10 steps left, cl sh (the submul loop
;  uses rcx too: sh is reloaded from the home slot of r8 per step), r12 d1, r13 d0, r14 v,
;  rbx q^; rax / rdx / rbp / r11 / r15 scratch. R's address waits in the home slot of r9.
; =============================================================================

global Fullint64DivWIN_recip
Fullint64DivWIN_recip:
	push rbx
	push rbp
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	mov [rsp+60h], r9           ; R (home slot of r9: 20h + 8 pushes)
	movsxd r10, dword [rsp+68h] ; lenA
	movsxd r9, dword [rsp+70h]  ; lenB
	mov rdi, [rsp+78h]          ; work
	mov rsi, rdx                ; B

	; U = [0, A]
	mov qword [rdi], 0
	xor eax, eax
.copyA:
	mov rdx, [rcx + rax*8]
	mov [rdi + rax*8 + 8], rdx
	inc rax
	cmp rax, r10
	jb .copyA
	sub r10, r9
	inc r10                     ; steps = lenA - lenB + 1

	; d1, d0: the top two words of B normalized
	mov r12, [rsi]
	bsr rcx, r12
	xor ecx, 63                 ; sh
	mov r13, [rsi + 8]
	xor eax, eax
	cmp r9, 2
	je .d0
	mov rax, [rsi + 16]
.d0:
	shld r12, r13, cl           ; d1
	shld r13, rax, cl           ; d0
	mov [rsp+58h], ecx          ; sh (home slot of r8): rcx is a temp in the submul loop

	; v: 2-by-1 reciprocal of d1, then adjusted for d0
	mov rdx, r12
	not rdx
	mov rax, -1
	div r12
	mov r14, rax
	mov rax, r12
	imul rax, r14               ; p = d1 * v (mod b)
	add rax, r13                ; p += d0
	jnc .inv1
	dec r14
	cmp rax, r12
	jb .inv0
	dec r14
	sub rax, r12
.inv0:
	sub rax, r12
.inv1:
	mov rbx, rax
	mov rax, r13
	mul r14                     ; t1:t0 = d0 * v
	add rbx, rdx                ; p += t1
	jnc .invDone
	dec r14
	cmp rbx, r12
	jb .invDone
	ja .inv2
	cmp rax, r13
	jb .invDone
.inv2:
	dec r14
.invDone:

.step:
	; n2, n1, n0: the top three words of W normalized
	mov rbx, [rdi]
	mov r11, [rdi + 8]
	mov r15, [rdi + 16]
	xor eax, eax
	cmp r9, 2
	je .w3                      ; W has three words: nothing below them in the window
	mov rax, [rdi + 24]
.w3:
	shld rbx, r11, cl
	shld r11, r15, cl
	shld r15, rax, cl
	cmp rbx, r12
	jne .estimate
	cmp r11, r13
	je .qMax                    ; (n2, n1) == (d1, d0)

.estimate:                      ; 3-by-2: (n2 n1 n0) / (d1 d0) -> rbx
	mov rax, r14
	mul rbx                     ; rdx:rax = v * n2
	add rax, r11
	adc rdx, rbx                ; (q : q0) += (n2 : n1)
	mov rbx, rdx                ; q
	mov rbp, rax                ; q0
	mov rax, r12
	imul rax, rbx
	sub r11, rax                ; r1 = n1 - d1 * q
	sub r15, r13
	sbb r11, r12                ; (r1 : r0) = (r1 : n0) - (d1 : d0)
	mov rax, r13
	mul rbx                     ; t1:t0 = d0 * q
	sub r15, rax
	sbb r11, rdx                ; (r1 : r0) -= (t1 : t0)
	inc rbx                     ; q + 1
	cmp r11, rbp
	sbb rax, rax
	not rax                     ; mask = r1 >= q0
	add rbx, rax                ; q - 1
	mov rdx, rax
	and rax, r13
	and rdx, r12
	add r15, rax
	adc r11, rdx                ; (r1 : r0) += mask & (d1 : d0)
	cmp r11, r12
	jae .estimateFix            ; rare: (r1 : r0) >= (d1 : d0)

.submul:                        ; W -= q^ * B, from the low end
	test rbx, rbx
	jz .stored                  ; q^ = 0 (the top word of a normalized divisor's quotient): W stays
	mov rbp, r9
	xor r11d, r11d              ; carry
	test ebp, 1
	jz .submulPairs
	mov rax, [rsi + rbp*8 - 8]  ; odd lenB: the lowest word alone
	mul rbx
	sub [rdi + rbp*8], rax
	adc rdx, 0
	mov r11, rdx
	dec rbp
.submulPairs:                   ; two words a pass: the product carries and the borrow
	mov rax, [rsi + rbp*8 - 8]  ; meet once per pass (rcx free here: sh reloaded per step)
	mul rbx
	add rax, r11
	adc rdx, 0
	mov r15, rax                ; low word's product, low half
	mov rcx, rdx
	mov rax, [rsi + rbp*8 - 16]
	mul rbx
	add rax, rcx                ; + the low word's high half
	adc rdx, 0
	sub [rdi + rbp*8], r15
	sbb [rdi + rbp*8 - 8], rax
	adc rdx, 0
	mov r11, rdx
	sub rbp, 2
	jnz .submulPairs
	sub [rdi], r11
	jc .addBack                 ; rare: q^ was one too many
.stored:
	mov [r8], rbx
	add r8, 8
	add rdi, 8                  ; the next window: one word down
	mov ecx, [rsp+58h]          ; sh
	dec r10
	jnz .step

	; the remainder: the last window's low lenB words (its top word is 0) -> R[2 ..]
	mov rdx, [rsp+60h]
	xor eax, eax
	mov [rdx], rax
	mov [rdx + 8], rax
	xor ecx, ecx
.copyR:
	mov rax, [rdi + rcx*8]
	mov [rdx + rcx*8 + 16], rax
	inc rcx
	cmp rcx, r9
	jb .copyR

	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbp
	pop rbx
	ret

;------------------------------------------------------------------------------
; cold paths
;------------------------------------------------------------------------------
.qMax:
	mov rbx, -1
	jmp .submul

.estimateFix:                   ; flags of cmp r1, d1
	ja .estimateInc
	cmp r15, r13
	jb .submul
.estimateInc:
	inc rbx
	jmp .submul

.addBack:                       ; W < 0: q^ - 1, W += B (until the carry comes out of the top)
	dec rbx
	mov rbp, r9
	xor eax, eax                ; CF = 0
.addBackLoop:
	mov rax, [rsi + rbp*8 - 8]
	adc [rdi + rbp*8], rax
	dec rbp
	jnz .addBackLoop
	adc qword [rdi], 0
	jnc .addBack
	jmp .stored
