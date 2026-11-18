
default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  The two halves of a Schönhage–Strassen butterfly, modulo p = 2^N + 1, N = 64·(n1 - 1):
;  values in n1 words (big-endian: word 0 the top), in [0, 2^N]. BMI2 + ADX; the callers
;  (Fft.cs / Fft.cpp) fall back to portable code without them.
;
;  void Fullint64FftAddSubWIN_adx(u, v, int n1)
;    u, v = u + v, u - v (mod p), in place, one pass: the sum on the adcx (CF) chain, the
;    difference u + ~v + 1 on the adox (OF) chain; loop control by lea / jrcxz only.
;    Then the sum's top word t > 0: u = low - t (+ p if that borrows); a negative difference
;    (no carry out of u + ~v + 1): v = v + p = its low part + 1, top 0 (+ the carry).
;
;  void Fullint64FftShlModWIN_bmi2(x, dst, int n1, int s)      0 <= s < 2N, dst != x
;    dst = x·2^s mod p in one pass. s >= N: s - N and the result negated (2^N = -1).
;    s = 64·ws + bs; sh(j) = word j (from the low end) of x << bs. With w = n1 - 1:
;      x·2^s = L + H·2^N == L - H,   L = (x << s) mod 2^N,  H = the words above
;      out word i:  i < ws:  0 - sh(i + w - ws)
;                   i = ws:  sh(0) - sh(w)
;                   i > ws:  sh(i - ws)
;    (negated: H - L), one sbb chain from the low end (shlx / shrx / lea / dec keep CF);
;    a borrow out of the top: + p = the low part + 1.
; =============================================================================

; the low part (words 1 .. n1-1 of the n1 at %1, %2 = n1 - 1) + 1, the top word = 0 or the
; carry out (r9, flags clobbered)
%macro LOW_PLUS_ONE 2
	mov qword [%1], 0
	lea r9, [%1 + %2*8]
%%inc:
	add qword [r9], 1
	jnc %%done
	lea r9, [r9 - 8]
	cmp r9, %1
	ja %%inc
	mov qword [%1], 1
%%done:
%endmacro

; jrcxz to a label out of the short range: through a jmp (no flags touched either way)
%macro JRCXZ_FAR 1
	jrcxz %%far
	jmp %%near
%%far:
	jmp %1
%%near:
%endmacro

; one word of AddSub at index rsi (+ %1 bytes): u + v on the CF chain, u + ~v on the OF chain
%macro ADDSUB_WORD 1
	mov rax, [r10 + rsi*8 + %1]
	mov rbx, [rdx + rsi*8 + %1]
	mov r9, rax
	adcx rax, rbx               ; u + v
	not rbx
	adox r9, rbx                ; u - v
	mov [r10 + rsi*8 + %1], rax
	mov [rdx + rsi*8 + %1], r9
%endmacro
ENTRY Fullint64FftAddSubWIN_adx
	WIN64_ARGS
	push rbx
	push rsi
	push rdi
	push rbp
	push r12
	push r13
	push r14
	push r15
	mov r10, rcx                ; u
	movsxd r8, r8d              ; n1
	lea r11, [r8 - 1]           ; w
	; blocks of four words from the low end: the four sums on one adc chain, then the four
	; differences on one sbb chain (u read again), the carry and the borrow kept as 0 / -1
	; masks between blocks (adcx / adox interleaved word by word ran at ~3 cycles a word on
	; Zen 3: both chains write the one flags register)
	lea rsi, [r10 + r11*8]      ; u: the low word
	lea rdi, [rdx + r11*8]      ; v: the low word
	mov rcx, r8
	shr rcx, 2                  ; groups of four
	and r8, 3                   ; then single words
	xor ebx, ebx                ; carry mask
	xor ebp, ebp                ; borrow mask
	jrcxz .singles
.quad:
	mov rax, [rsi]
	mov r9, [rsi - 8]
	mov r12, [rsi - 16]
	mov r13, [rsi - 24]
	add rbx, rbx                ; CF = the carry
	adc rax, [rdi]
	adc r9, [rdi - 8]
	adc r12, [rdi - 16]
	adc r13, [rdi - 24]
	sbb rbx, rbx
	mov r14, [rsi]
	mov r15, [rsi - 8]
	add rbp, rbp                ; CF = the borrow
	sbb r14, [rdi]
	sbb r15, [rdi - 8]
	mov [rdi], r14
	mov [rdi - 8], r15
	mov r14, [rsi - 16]
	mov r15, [rsi - 24]
	sbb r14, [rdi - 16]
	sbb r15, [rdi - 24]
	sbb rbp, rbp
	mov [rdi - 16], r14
	mov [rdi - 24], r15
	mov [rsi], rax
	mov [rsi - 8], r9
	mov [rsi - 16], r12
	mov [rsi - 24], r13
	lea rsi, [rsi - 32]
	lea rdi, [rdi - 32]
	dec rcx
	jnz .quad
.singles:
	mov rcx, r8
	jrcxz .done
.one:
	mov rax, [rsi]
	mov r9, rax
	add rbx, rbx
	adc rax, [rdi]
	sbb rbx, rbx
	add rbp, rbp
	sbb r9, [rdi]
	sbb rbp, rbp
	mov [rsi], rax
	mov [rdi], r9
	lea rsi, [rsi - 8]
	lea rdi, [rdi - 8]
	dec rcx
	jnz .one
.done:
	test rbp, rbp
	setz bl                     ; 1: u >= v (no borrow out)

	; the sum: top word t > 0 -> low - t, + p on a borrow
	mov rax, [r10]
	test rax, rax
	jz .sumDone
	mov qword [r10], 0
	lea r9, [r10 + r11*8]
	sub [r9], rax
	jnc .sumDone
.sumBorrow:
	lea r9, [r9 - 8]
	cmp r9, r10
	jbe .sumNegative
	sub qword [r9], 1
	jc .sumBorrow
	jmp .sumDone
.sumNegative:                   ; low - t < 0: + p = (low - t + 2^N) + 1
	LOW_PLUS_ONE r10, r11
.sumDone:

	; the difference: negative -> + p
	test bl, bl
	jnz .diffDone
	LOW_PLUS_ONE rdx, r11
.diffDone:
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbp
	pop rdi
	pop rsi
	pop rbx
	WRET

;------------------------------------------------------------------------------

; rax = sh(j), the word of x at %1 (x_j) and the one below it at %1 + 8 (x_(j-1)):
;   x_j << bs | x_(j-1) >> (64 - bs)   (r11 = bs, r13 = 1, r10 = 63 - bs: >> 1 >> (63 - bs)
;   is 0 for bs = 0); no flags touched (rbx scratch)
%macro SH 1
	mov rax, [%1]
	shlx rax, rax, r11
	mov rbx, [%1 + 8]
	shrx rbx, rbx, r13
	shrx rbx, rbx, r10
	lea rax, [rax + rbx]
%endmacro

; one output word of region A (i < ws: 0 - H, negated: H) from x at r9 - %2 into dst at rdx - %2
%macro OUT_A 2
	SH r9 - %2
%if %1
	sbb rax, 0
	mov [rdx - %2], rax
%else
	mov rbx, 0
	sbb rbx, rax
	mov [rdx - %2], rbx
%endif
%endmacro

; one output word of region C (i > ws: L, negated: 0 - L)
%macro OUT_C 2
	SH r9 - %2
%if %1
	mov rbx, 0
	sbb rbx, rax
	mov [rdx - %2], rbx
%else
	sbb rax, 0
	mov [rdx - %2], rax
%endif
%endmacro

; the three regions; %1 = 0: L - H, 1: H - L. rsi x, rdi dst, r14 w, r12 ws, r15 = w - 1 - ws
%macro SHLMOD_BODY 1
	lea rdx, [rdi + r14*8]      ; dst word i = 0 (the lowest)
	lea r9, [rsi + r12*8]       ; x word j = w - ws
	mov rbx, r15
	shr rbx, 2
	push rbx                    ; region C: groups of four (r15 = w - 1 - ws words)
	mov rbx, r15
	and rbx, 3
	push rbx                    ; region C: single words, popped first
	mov r8, r12
	shr r8, 2                   ; groups of four (counted before clc: shr / and set CF)
	mov rcx, r12
	and rcx, 3
	clc
	jrcxz %%aQuads
%%aOne:                         ; i < ws: 0 - H, single words first
	OUT_A %1, 0
	lea r9, [r9 - 8]
	lea rdx, [rdx - 8]
	dec rcx                     ; (dec keeps CF)
	jnz %%aOne
%%aQuads:
	mov rcx, r8
	JRCXZ_FAR %%regionB
%%aQuad:
	OUT_A %1, 0
	OUT_A %1, 8
	OUT_A %1, 16
	OUT_A %1, 24
	lea r9, [r9 - 32]
	lea rdx, [rdx - 32]
	dec rcx
	jnz %%aQuad
%%regionB:                      ; i = ws: sh(0) - sh(w)
	SH rsi
	mov rbx, [rsi + r14*8]
	shlx rbx, rbx, r11
%if %1
	sbb rax, rbx
	mov [rdx], rax
%else
	sbb rbx, rax
	mov [rdx], rbx
%endif
	lea rdx, [rdx - 8]
	lea r9, [rsi + r14*8 - 8]   ; x word j = 1
	pop rcx                     ; region C single words (pushed before clc; pop keeps CF)
	jrcxz %%cQuads
%%cOne:                         ; i > ws: L
	OUT_C %1, 0
	lea r9, [r9 - 8]
	lea rdx, [rdx - 8]
	dec rcx
	jnz %%cOne
%%cQuads:
	pop rcx                     ; region C groups of four
	JRCXZ_FAR %%top
%%cQuad:
	OUT_C %1, 0
	OUT_C %1, 8
	OUT_C %1, 16
	OUT_C %1, 24
	lea r9, [r9 - 32]
	lea rdx, [rdx - 32]
	dec rcx
	jnz %%cQuad
%%top:
	mov qword [rdi], 0
	jnc %%done
	LOW_PLUS_ONE rdi, r14       ; a borrow out: + p
%%done:
%endmacro
ENTRY Fullint64FftShlModWIN_bmi2
	WIN64_ARGS
	push rbx
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	mov rsi, rcx                ; x
	mov rdi, rdx                ; dst
	movsxd r8, r8d
	lea r14, [r8 - 1]           ; w
	movsxd rax, r9d             ; s
	mov r9, r14
	shl r9, 6                   ; N
	xor ecx, ecx                ; negate
	cmp rax, r9
	jb .noNegate
	sub rax, r9
	mov ecx, 1
.noNegate:
	mov r12, rax
	shr r12, 6                  ; ws
	and eax, 63
	mov r11, rax                ; bs
	mov r10d, 63
	sub r10, rax                ; 63 - bs
	mov r13d, 1
	mov r15, r14
	sub r15, r12
	dec r15                     ; w - 1 - ws
	test ecx, ecx
	jnz .negated
	SHLMOD_BODY 0
	jmp .return
.negated:
	SHLMOD_BODY 1
.return:
	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
