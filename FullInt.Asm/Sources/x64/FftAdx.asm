
default rel
section .text

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

global Fullint64FftAddSubWIN_adx
Fullint64FftAddSubWIN_adx:
	push rbx
	push rsi
	mov r10, rcx                ; u
	movsxd r8, r8d              ; n1
	lea r11, [r8 - 1]           ; w
	mov rsi, r11                ; index from the low end word
	mov rcx, r8                 ; words left
	mov al, 7Fh
	add al, 1                   ; OF = 1 (the + 1 of u + ~v + 1), CF = 0
.loop:
	mov rax, [r10 + rsi*8]
	mov rbx, [rdx + rsi*8]
	mov r9, rax
	adcx rax, rbx               ; u + v
	not rbx
	adox r9, rbx                ; u - v
	mov [r10 + rsi*8], rax
	mov [rdx + rsi*8], r9
	lea rsi, [rsi - 1]
	lea rcx, [rcx - 1]
	jrcxz .done
	jmp .loop
.done:
	seto bl                     ; 1: u >= v

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
	pop rsi
	pop rbx
	ret

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

; the three regions; %1 = 0: L - H, 1: H - L. rsi x, rdi dst, r14 w, r12 ws, r15 = w - 1 - ws
%macro SHLMOD_BODY 1
	lea rdx, [rdi + r14*8]      ; dst word i = 0 (the lowest)
	mov rcx, r12
	clc
	jrcxz %%regionB
	lea r9, [rsi + r12*8]       ; x word j = w - ws
%%regionA:                      ; i < ws: 0 - H
	SH r9
%if %1
	sbb rax, 0
	mov [rdx], rax
%else
	mov rbx, 0
	sbb rbx, rax
	mov [rdx], rbx
%endif
	lea r9, [r9 - 8]
	lea rdx, [rdx - 8]
	dec rcx
	jnz %%regionA
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
	mov rcx, r15
	jrcxz %%top
	lea r9, [rsi + r14*8 - 8]   ; x word j = 1
%%regionC:                      ; i > ws: L
	SH r9
%if %1
	mov rbx, 0
	sbb rbx, rax
	mov [rdx], rbx
%else
	sbb rax, 0
	mov [rdx], rax
%endif
	lea r9, [r9 - 8]
	lea rdx, [rdx - 8]
	dec rcx
	jnz %%regionC
%%top:
	mov qword [rdi], 0
	jnc %%done
	LOW_PLUS_ONE rdi, r14       ; a borrow out: + p
%%done:
%endmacro

global Fullint64FftShlModWIN_bmi2
Fullint64FftShlModWIN_bmi2:
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
	ret
