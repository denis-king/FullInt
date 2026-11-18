
default rel
%include "abi.inc"
CODE_SECTION

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
ENTRY Fullint64Div2longWIN_recip
	WIN64_ARGS
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
	WRET


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

;------------------------------------------------------------------------------
;  u64 Fullint64DivQWIN_recip(A, B, Q, R, int lenA, int lenB, u64* work)   (and _recipadx)
;  The quotient alone, approximately (GMP's sbpi1_divappr_q): Q' ~ floor(A*beta / B), lenA -
;  lenB + 2 words (the quotient and one guard word below it), R unused, work: lenA + 2
;  words. U = [0, A, 0]; the window moves down as above, but the multiply-subtract pass
;  leaves out every word of U below index L = steps + 2 (counted from U's top; a step uses
;  only the top min(lenB, steps left + 2) words of B): the last lenB - 3 steps get shorter
;  and shorter (2N / N: about half the passes' work). A left-out product q^ * (B's low words)
;  is below 2 beta^(1 - kept) of B at its step, 2 / beta of B at the last window's scale; all of
;  them below 2 lenB / beta of B: a q^ can be off only by a window that close to a boundary,
;  and the add-back corrects it as for any estimate, so
;    Q' = floor(A*beta / B) + {-1, 0, 1}
;  (the final window is in [0, B + that error)), returns 0. The caller takes Q'[0 .. steps - 2]
;  when the guard word is not 0 or beta - 1, otherwise checks that against A by one product.
;  (n2 n1) == (d1 d0) gives q^ = beta - 1 as above (with the error the window may then stay a
;  little above B: the next words and the bound take that). A window above B's top words
;  (n2 n1 > d1 d0) or a nonzero top word after the pass - only the left-out error can do
;  that - ends it: the rest of Q' is beta - 1, returns 1 (the caller divides exactly).
;------------------------------------------------------------------------------

; the long division; %1 the name, %2 = 1: the multiply-subtract pass with mulx / adcx / adox,
; %3 = 1: the approximate quotient (above)
%macro DIV_RECIP_LONG 3
ENTRY %1
	WIN64_ARGS
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
%if %3
	mov qword [rdi + rax*8 + 8], 0   ; U = [0, A, 0]
	inc r10                     ; one more step: the guard word
%endif

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
%if %3
	cmp rbx, r12
	jb .estimate
	ja .saturate                ; (n2, n1) > (d1, d0): the left-out error only
	cmp r11, r13
	je .qMax                    ; (n2, n1) == (d1, d0): q^ = beta - 1 (exact data, all-ones B)
	ja .saturate
%else
	cmp rbx, r12
	jne .estimate
	cmp r11, r13
	je .qMax                    ; (n2, n1) == (d1, d0)
%endif

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
%if %2
	; mulx / adcx / adox: the product q^·B[i] + the previous high half on the CF chain, then
	; W[i] + ~that + 1 on the OF chain (a subtraction as an add); loop control by lea / jrcxz
	mov rdx, rbx
	mov rcx, r9                 ; index (rcx free here: sh reloaded per step)
%if %3
	lea rax, [r10 + 2]          ; the words of B kept: min(lenB, steps left + 2)
	cmp rcx, rax
	cmova rcx, rax
%endif
	xor r11d, r11d              ; the previous high half; OF = 1 (the + 1), CF = 0 set below
	test ecx, 1                 ; (flags: only OF / CF matter from here, and test clears both:
	jz .submulAdxPairs          ;  set them again below)
	mov al, 7Fh
	add al, 1
	mulx r15, rax, [rsi + rcx*8 - 8]   ; an odd count: one word first
	adcx rax, r11
	mov r11, r15
	not rax
	adox rax, [rdi + rcx*8]
	mov [rdi + rcx*8], rax
	lea rcx, [rcx - 1]
	jmp .submulAdxPairs2
.submulAdxPairs:
	mov al, 7Fh
	add al, 1
.submulAdxPairs2:               ; two words a pass
	mulx r15, rax, [rsi + rcx*8 - 8]
	adcx rax, r11
	not rax
	adox rax, [rdi + rcx*8]
	mov [rdi + rcx*8], rax
	mulx r11, rax, [rsi + rcx*8 - 16]
	adcx rax, r15
	not rax
	adox rax, [rdi + rcx*8 - 8]
	mov [rdi + rcx*8 - 8], rax
	lea rcx, [rcx - 2]
	jrcxz .submulAdxTop
	jmp .submulAdxPairs2
.submulAdxTop:
	adcx r11, rcx               ; the top of the product (+ CF; rcx = 0)
	not r11
	mov rax, [rdi]
	adox rax, r11
	mov [rdi], rax
	seto al                     ; 0: a borrow out of the top - W < 0
	test al, al
	jz .addBack
%if %3
	cmp qword [rdi], 0
	jne .saturate               ; the window reached B (the left-out error)
%endif
%else
	mov rbp, r9
%if %3
	lea rax, [r10 + 2]          ; the words of B kept: min(lenB, steps left + 2)
	cmp rbp, rax
	cmova rbp, rax
%endif
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
%if %3
	jnz .saturate               ; the window reached B (the left-out error)
%endif
%endif
.stored:
	mov [r8], rbx
	add r8, 8
	add rdi, 8                  ; the next window: one word down
	mov ecx, [rsp+58h]          ; sh
	dec r10
	jnz .step
%if %3
	xor eax, eax                ; 0: Q' within 1
.done:
%else

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
%endif

	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbp
	pop rbx
	WRET

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

%if %3
.saturate:                      ; the rest of Q' (this word on) is beta - 1
	mov qword [r8], -1
	add r8, 8
	dec r10
	jnz .saturate
	mov eax, 1                  ; 1: saturated
	jmp .done
%endif

.addBack:                       ; W < 0: q^ - 1, W += B (until the carry comes out of the top)
	dec rbx
	mov rbp, r9
%if %3
	lea rax, [r10 + 2]          ; the same words of B as the pass
	cmp rbp, rax
	cmova rbp, rax
%endif
	xor eax, eax                ; CF = 0
.addBackLoop:
	mov rax, [rsi + rbp*8 - 8]
	adc [rdi + rbp*8], rax
	dec rbp
	jnz .addBackLoop
	adc qword [rdi], 0
	jnc .addBack
	jmp .stored
%endmacro

DIV_RECIP_LONG Fullint64DivWIN_recip, 0, 0
DIV_RECIP_LONG Fullint64DivWIN_recipadx, 1, 0     ; BMI2 + ADX
DIV_RECIP_LONG Fullint64DivQWIN_recip, 0, 1
DIV_RECIP_LONG Fullint64DivQWIN_recipadx, 1, 1

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
