; =============================================================================
;  x86 (32-bit) division with a precomputed reciprocal (Moller, Granlund 2011), next to
;  Div.asm / Div2Long.asm (the baseline, kept as is). cdecl. Plain i386 + mul / shld:
;  any CPU; picked unless FULLINT_CPU=baseline.
;
;  On x86 the 64-bit word operations are built from 32-bit ones (div r64 -> fi_div128,
;  ~100+ cycles; mul r64 -> four mul r32), so these kernels work in 32-bit digits, the
;  native size: the same number of mul r32 per word product, and no 128/64 division at all.
;  Words stay big-endian (index 0 = most significant), each one little-endian in memory, so
;  the big-endian dword j of a word array is at [p + 4 * (j ^ 1)].
;
;  The quotient digit: 3-by-2 division with the reciprocal v of the top two divisor digits
;  (normalized), v = floor((b^3 - 1) / (d1 b + d0)) - b, b = 2^32 (one div r32 for the
;  2-by-1 reciprocal of d1, adjusted for d0 as GMP's invert_pi1; checked against the exact
;  value on 200000 divisors).
; =============================================================================

section .text

extern Fullint64Div2longWin

; v = 3-by-2 reciprocal of (L_D1, L_D0) -> L_V; eax, ebx, edx clobbered (ecx kept)
%macro INVERT_PI1 0
	mov edx, [L_D1]
	not edx
	mov eax, -1
	div dword [L_D1]            ; 2-by-1 reciprocal of d1
	mov [L_V], eax
	mov eax, [L_D1]
	imul eax, [L_V]             ; p = d1 * v (mod b)
	add eax, [L_D0]
	jnc %%inv1
	dec dword [L_V]
	cmp eax, [L_D1]
	jb %%inv0
	dec dword [L_V]
	sub eax, [L_D1]
%%inv0:
	sub eax, [L_D1]
%%inv1:
	mov ebx, eax
	mov eax, [L_D0]
	mul dword [L_V]             ; t1:t0 = d0 * v
	add ebx, edx
	jnc %%done
	dec dword [L_V]
	cmp ebx, [L_D1]
	jb %%done
	ja %%dec2
	cmp eax, [L_D0]
	jb %%done
%%dec2:
	dec dword [L_V]
%%done:
%endmacro


; =============================================================================
;  void Fullint64Div2longWIN_recip(A, ulong d, Q, ulong* r, int lenA)
;  The contract of Fullint64Div2longWin (Q may alias A). d < 2^32: a tail jump to it (two
;  div r32 per word, native). d >= 2^32: d is two 32-bit digits, so every step is one
;  3-by-2 division that also gives the exact remainder - no multiply-subtract pass:
;    (r1 r0 | next digit of A << sh) / (d1 d0) -> one quotient digit, (r1 r0)
;  (was fi_div128 per word: Knuth D on 32-bit digits with div r32 and its corrections).
;
;  Registers: cl sh; ebx:esi remainder (r1:r0); edi n0, then r0; ebp index / q0; eax, edx.
; =============================================================================

%define L_D1   esp
%define L_D0   esp+4
%define L_V    esp+8
%define L_IDX  esp+12
%define L_N    esp+16
%define FR2    20
%define A2_A   esp+FR2+20
%define A2_DLO esp+FR2+24
%define A2_DHI esp+FR2+28
%define A2_Q   esp+FR2+32
%define A2_R   esp+FR2+36
%define A2_LA  esp+FR2+40

global Fullint64Div2longWIN_recip
Fullint64Div2longWIN_recip:
	cmp dword [esp+12], 0       ; d high
	je Fullint64Div2longWin     ; d < 2^32: the baseline, arguments untouched
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FR2

	; d normalized: (d1 d0) = d << sh, top bit set
	mov ebx, [A2_DHI]
	mov eax, [A2_DLO]
	bsr ecx, ebx
	xor ecx, 31                 ; sh
	shld ebx, eax, cl
	shl eax, cl
	mov [L_D1], ebx
	mov [L_D0], eax
	INVERT_PI1

	; S = A << sh as 2 lenA + 1 digits; (r1 r0) = (0, S[0]), then S[1 ..] one digit a step
	mov eax, [A2_LA]
	add eax, eax
	mov [L_N], eax
	mov edi, [A2_A]
	xor esi, esi
	mov eax, [edi + 4]          ; A dword 0 (the high dword of A[0])
	shld esi, eax, cl
	xor ebx, ebx
	mov dword [L_IDX], 1
.step:
	mov ebp, [L_IDX]
	mov edi, [A2_A]
	lea eax, [ebp - 1]
	xor eax, 1
	mov eax, [edi + eax*4]      ; A dword i-1
	xor edx, edx
	cmp ebp, [L_N]
	jae .lastDigit
	mov edx, ebp
	xor edx, 1
	mov edx, [edi + edx*4]      ; A dword i (read before Q dword i-1 is written: Q may be A)
.lastDigit:
	shld eax, edx, cl
	mov edi, eax                ; n0

	; 3-by-2: (ebx esi edi) / (d1 d0) -> ebx, remainder (esi edi)
	mov eax, [L_V]
	mul ebx
	add eax, esi
	adc edx, ebx                ; (q : q0) = v n2 + (n2 : n1)
	mov ebx, edx
	mov ebp, eax
	mov eax, [L_D1]
	imul eax, ebx
	sub esi, eax                ; r1 = n1 - d1 q
	sub edi, [L_D0]
	sbb esi, [L_D1]             ; (r1 : r0) = (r1 : n0) - (d1 : d0)
	mov eax, [L_D0]
	mul ebx
	sub edi, eax
	sbb esi, edx                ; -= d0 q
	inc ebx
	cmp esi, ebp
	sbb eax, eax
	not eax                     ; mask = r1 >= q0
	add ebx, eax
	mov edx, eax
	and eax, [L_D0]
	and edx, [L_D1]
	add edi, eax
	adc esi, edx
	cmp esi, [L_D1]
	jae .fix                    ; rare
.fixed:
	mov ebp, [L_IDX]
	lea eax, [ebp - 1]
	xor eax, 1
	mov edx, [A2_Q]
	mov [edx + eax*4], ebx      ; Q dword i-1
	mov ebx, esi
	mov esi, edi
	inc ebp
	mov [L_IDX], ebp
	cmp ebp, [L_N]
	jbe .step

	shrd esi, ebx, cl           ; the remainder, not normalized
	shr ebx, cl
	mov eax, [A2_R]
	mov [eax], esi
	mov [eax + 4], ebx
	add esp, FR2
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

.fix:                           ; flags of cmp r1, d1
	ja .fixInc
	cmp edi, [L_D0]
	jb .fixed
.fixInc:
	inc ebx
	sub edi, [L_D0]
	sbb esi, [L_D1]
	jmp .fixed


; =============================================================================
;  void Fullint64DivWIN_recip(A, B, Q, R, int lenA, int lenB, u64* work)
;  lenA > lenB >= 2, B without leading zero words; Q: lenA - lenB + 1 words; R: lenB + 2
;  words as Fullint64DivWin leaves it (R[0] = R[1] = 0, the remainder in R[2 ..]);
;  work: lenA + lenB + 2 words of scratch.
;
;  Knuth D in 32-bit digits, as x64/DivRecip.asm in 64-bit ones:
;    work = [U: 0, the 2 lenA digits of A][pad][D: the nb digits of B without a zero top
;    dword]; the window W = U[j .. j + nb] moves down by a pointer;
;    q^ from the top three digits of W and the top two of D normalized by sh (shld on the
;    fly) by the 3-by-2 reciprocal division; q^ = 0 skips the pass; W -= q^ * D;
;    negative (rare): q^ - 1, W += D. (n2 n1) == (d1 d0) gives q^ = b - 1.
;  2 lenA + 1 - nb quotient digits go right-aligned into Q (its top dword 0 when nb = 2 lenB).
;
;  Registers: ebp W; estimate: ebx n2 -> q^, ecx n1, esi n0, edi q0; multiply-subtract:
;  ebx q^, esi D, ecx index, edi carry.
; =============================================================================

%define L_SH    esp+12
%define L_NB    esp+16
%define L_STEPS esp+20
%define L_QIDX  esp+24
%define L_DPTR  esp+28
%define L_K0    esp+32
%define FRL     36
%define AL_A    esp+FRL+20
%define AL_B    esp+FRL+24
%define AL_Q    esp+FRL+28
%define AL_R    esp+FRL+32
%define AL_LA   esp+FRL+36
%define AL_LB   esp+FRL+40
%define AL_W    esp+FRL+44

%ifndef V2MIN
%define V2MIN 10                ; divisors of V2MIN words and more: the word version below
                                ; (measured: 2-8 words the digit version even or ahead with long
                                ; quotients, 12 and more the word version 1.1-1.5x ahead)
%endif

global Fullint64DivWIN_recip
Fullint64DivWIN_recip:
	cmp dword [esp+24], V2MIN   ; lenB
	jae Fullint64DivWIN_recip_v2

global Fullint64DivWIN_recip_v1
Fullint64DivWIN_recip_v1:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRL

	; U = [0, A's digits]
	mov esi, [AL_A]
	mov edi, [AL_W]
	mov dword [edi], 0
	mov ecx, [AL_LA]
	add ecx, ecx
	xor eax, eax
.copyA:
	mov edx, eax
	xor edx, 1
	mov edx, [esi + edx*4]
	mov [edi + eax*4 + 4], edx
	inc eax
	cmp eax, ecx
	jb .copyA
	lea ebp, [edi + ecx*4 + 8]  ; D after U (2 lenA + 1 digits) and a pad dword
	mov [L_DPTR], ebp

	; D = B's digits from the first nonzero dword (k0 = 1 when B[0] < 2^32)
	mov esi, [AL_B]
	xor ebx, ebx
	cmp dword [esi + 4], 0
	jne .k0
	inc ebx
.k0:
	mov [L_K0], ebx
	mov ecx, [AL_LB]
	add ecx, ecx
	sub ecx, ebx
	mov [L_NB], ecx
	xor eax, eax
.copyD:
	lea edx, [eax + ebx]
	xor edx, 1
	mov edx, [esi + edx*4]
	mov [ebp + eax*4], edx
	inc eax
	cmp eax, ecx
	jb .copyD

	mov eax, [AL_LA]
	add eax, eax
	inc eax
	sub eax, ecx
	mov [L_STEPS], eax          ; 2 lenA + 1 - nb quotient digits
	mov eax, 1
	sub eax, ebx
	mov [L_QIDX], eax           ; Q dword of the first one: 1 - k0
	mov edx, [AL_Q]
	mov dword [edx + 4], 0      ; Q dword 0 (k0 = 0: no digit there; k0 = 1: written below)

	; sh, (d1 d0) = the top two digits of D normalized, v
	mov ebx, [ebp]
	bsr ecx, ebx
	xor ecx, 31
	mov [L_SH], ecx
	mov eax, [ebp + 4]
	mov edx, [ebp + 8]
	shld ebx, eax, cl
	shld eax, edx, cl
	mov [L_D1], ebx
	mov [L_D0], eax
	INVERT_PI1

	mov ebp, [AL_W]             ; W: the first window
.step:
	mov ecx, [L_SH]
	mov ebx, [ebp]
	mov eax, [ebp + 4]
	shld ebx, eax, cl           ; n2
	mov esi, [ebp + 8]
	shld eax, esi, cl           ; n1
	mov edi, [ebp + 12]
	shld esi, edi, cl           ; n0 (nb >= 3: W has at least 4 digits)
	mov ecx, eax
	cmp ebx, [L_D1]
	jne .estimate
	cmp ecx, [L_D0]
	je .qMax

.estimate:                      ; 3-by-2: (ebx ecx esi) / (d1 d0) -> ebx
	mov eax, [L_V]
	mul ebx
	add eax, ecx
	adc edx, ebx
	mov ebx, edx                ; q
	mov edi, eax                ; q0
	mov eax, [L_D1]
	imul eax, ebx
	sub ecx, eax                ; r1 = n1 - d1 q
	sub esi, [L_D0]
	sbb ecx, [L_D1]
	mov eax, [L_D0]
	mul ebx
	sub esi, eax
	sbb ecx, edx                ; (r1 : r0)
	inc ebx
	cmp ecx, edi
	sbb eax, eax
	not eax
	add ebx, eax
	mov edx, eax
	and eax, [L_D0]
	and edx, [L_D1]
	add esi, eax
	adc ecx, edx
	cmp ecx, [L_D1]
	jae .estimateFix            ; rare

.submul:                        ; W -= q^ * D, from the low end
	test ebx, ebx
	jz .stored
	mov esi, [L_DPTR]
	mov ecx, [L_NB]
	xor edi, edi
.submulLoop:
	mov eax, [esi + ecx*4 - 4]
	mul ebx
	add eax, edi
	adc edx, 0
	sub [ebp + ecx*4], eax
	adc edx, 0
	mov edi, edx
	dec ecx
	jnz .submulLoop
	sub [ebp], edi
	jc .addBack                 ; rare: q^ one too many
.stored:
	mov eax, [L_QIDX]
	mov edx, eax
	xor edx, 1
	mov ecx, [AL_Q]
	mov [ecx + edx*4], ebx
	inc eax
	mov [L_QIDX], eax
	add ebp, 4                  ; the next window
	dec dword [L_STEPS]
	jnz .step

	; the remainder: the last window's low nb digits (at ebp now) -> R[2 ..], right-aligned
	mov edx, [AL_R]
	xor eax, eax
	mov [edx], eax
	mov [edx + 4], eax
	mov [edx + 8], eax
	mov [edx + 12], eax
	mov ecx, [AL_LB]
	add ecx, ecx
	mov ebx, [L_K0]
.copyR:                         ; R dword t: t < k0 ? 0 : the remainder's digit t - k0
	xor esi, esi
	mov edi, eax
	sub edi, ebx
	js .copyRZero
	mov esi, [ebp + edi*4]
.copyRZero:
	mov edi, eax
	xor edi, 1
	mov [edx + edi*4 + 16], esi
	inc eax
	cmp eax, ecx
	jb .copyR

	add esp, FRL
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

;------------------------------------------------------------------------------
; cold paths
;------------------------------------------------------------------------------
.qMax:
	mov ebx, -1
	jmp .submul

.estimateFix:                   ; flags of cmp r1, d1
	ja .estimateInc
	cmp esi, [L_D0]
	jb .submul
.estimateInc:
	inc ebx
	jmp .submul

.addBack:                       ; W < 0: q^ - 1, W += D until the carry comes out of the top
	dec ebx
	mov ecx, [L_NB]
	xor eax, eax                ; CF = 0
.addBackLoop:
	mov eax, [esi + ecx*4 - 4]
	adc [ebp + ecx*4], eax
	dec ecx
	jnz .addBackLoop
	adc dword [ebp], 0
	jnc .addBack
	jmp .stored


; =============================================================================
;  The word version (lenB >= 3; the same contract, work: lenA + lenB + 2 words).
;  In 32-bit digits the multiply-subtract pass is one carry chain through every digit
;  product - four chain steps per word product. Here, as in x64/DivRecip.asm, one quotient
;  WORD a step, and the pass as in the baseline Div.asm: per divisor word four independent
;  mul r32 and the borrow pushed into memory (two sbb 0 into the word above; rarely further).
;
;  work = [guard][U: 0, A] in words; the window W = U[j .. j + lenB] moves by a pointer, the
;  word before it is the guard (the previous window top word, 0 and no longer needed).
;  q^ = floor(T / S) exactly: T = the top 6 digits of W, S = the top 4 digits of B, both
;  normalized by sh (0..63: a = sh / 32 digits and b = sh mod 32 bits, shld on the fly), by
;  two 32-bit steps (5 digits / 4: the 3-by-2 reciprocal estimate, 4 digit products, add-back
;  while negative). T >= S * 2^64 only with the top 4 digits of T = S: then q^ = 2^64 - 1.
;  The true word is q^ or (rarely) q^ - 1: W -= q^ * B; while W < 0 (its top word is not 0):
;  q^ - 1, W += B.
; =============================================================================

%define V_A     esp+12
%define V_B     esp+16
%define V_S1    esp+20
%define V_S0    esp+24
%define QLO     esp+28
%define QHI     esp+32
%define P0      esp+36
%define P3      esp+40
%define V_STEPS esp+44
%define V_QP    esp+48
%define V_LB    esp+52
%define TT      esp+56          ; 8 dwords: the top digits of W, then T in place
%define TTOFF   56
%define FRV     88
%define VA_A    esp+FRV+20
%define VA_B    esp+FRV+24
%define VA_Q    esp+FRV+28
%define VA_R    esp+FRV+32
%define VA_LA   esp+FRV+36
%define VA_LB   esp+FRV+40
%define VA_W    esp+FRV+44

; one quotient digit of the 5 digits TT[o .. o + 4] by S (exact): -> ebx, TT[o + 1 ..] = the
; remainder (4 digits), TT[o] = 0. %1 = the byte offset 4 * o.
%macro MINISTEP 1
	mov ebx, [TT + %1]
	mov ecx, [TT + %1 + 4]
	mov esi, [TT + %1 + 8]
	cmp ebx, [L_D1]
	jne %%estimate
	cmp ecx, [L_D0]
	jne %%estimate
	mov ebx, -1                 ; (n2 n1) == (s3 s2)
	jmp %%sub
%%estimate:                     ; 3-by-2: (ebx ecx esi) / (s3 s2) -> ebx
	mov eax, [L_V]
	mul ebx
	add eax, ecx
	adc edx, ebx
	mov ebx, edx
	mov edi, eax
	mov eax, [L_D1]
	imul eax, ebx
	sub ecx, eax
	sub esi, [L_D0]
	sbb ecx, [L_D1]
	mov eax, [L_D0]
	mul ebx
	sub esi, eax
	sbb ecx, edx
	inc ebx
	cmp ecx, edi
	sbb eax, eax
	not eax
	add ebx, eax
	mov edx, eax
	and eax, [L_D0]
	and edx, [L_D1]
	add esi, eax
	adc ecx, edx
	cmp ecx, [L_D1]
	jb %%sub
	ja %%inc
	cmp esi, [L_D0]
	jb %%sub
%%inc:
	inc ebx
%%sub:                          ; TT[o ..] -= ebx * S
	mov eax, [V_S0]
	mul ebx
	sub [TT + %1 + 16], eax
	adc edx, 0
	mov edi, edx
	mov eax, [V_S1]
	mul ebx
	add eax, edi
	adc edx, 0
	sub [TT + %1 + 12], eax
	adc edx, 0
	mov edi, edx
	mov eax, [L_D0]
	mul ebx
	add eax, edi
	adc edx, 0
	sub [TT + %1 + 8], eax
	adc edx, 0
	mov edi, edx
	mov eax, [L_D1]
	mul ebx
	add eax, edi
	adc edx, 0
	sub [TT + %1 + 4], eax
	adc edx, 0
	sub [TT + %1], edx
	jnc %%done
%%addBack:                      ; negative: digit - 1, + S until the carry comes out
	dec ebx
	mov eax, [V_S0]
	add [TT + %1 + 16], eax
	mov eax, [V_S1]
	adc [TT + %1 + 12], eax
	mov eax, [L_D0]
	adc [TT + %1 + 8], eax
	mov eax, [L_D1]
	adc [TT + %1 + 4], eax
	adc dword [TT + %1], 0
	jnc %%addBack
%%done:
%endmacro

; %1 the name, %2 = 1: the approximate quotient (x64/DivRecip.asm, Fullint64DivQWIN_recip:
; Q' = lenA - lenB + 2 words ~ floor(A·β / B) within 1, returns 0; or saturated, returns 1;
; R unused; work: lenA + 3 words; lenA > lenB >= 3)
%macro DIV_RECIP_V2 2
global %1
%1:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRV

	; work = [guard][0][A]
	mov esi, [VA_A]
	mov edi, [VA_W]
	xor eax, eax
	mov [edi], eax
	mov [edi + 4], eax
	mov [edi + 8], eax
	mov [edi + 12], eax
	mov ecx, [VA_LA]
	xor edx, edx
.copyA:
	mov eax, [esi + edx*8]
	mov [edi + edx*8 + 16], eax
	mov eax, [esi + edx*8 + 4]
	mov [edi + edx*8 + 20], eax
	inc edx
	cmp edx, ecx
	jb .copyA
%if %2
	xor eax, eax                ; U = [0, A, 0]
	mov [edi + edx*8 + 16], eax
	mov [edi + edx*8 + 20], eax
%endif

	; sh = a * 32 + b: the leading zeros of B[0]
	mov esi, [VA_B]
	xor edx, edx
	mov eax, [esi + 4]
	test eax, eax
	jnz .shHigh
	mov eax, [esi]
	inc edx
.shHigh:
	bsr ecx, eax
	xor ecx, 31
	mov [V_A], edx
	mov [V_B], ecx

	; S = the top 4 digits of B normalized (B has >= 6 digits: lenB >= 3)
	mov eax, [esi + 4]
	mov [TT], eax
	mov eax, [esi]
	mov [TT + 4], eax
	mov eax, [esi + 12]
	mov [TT + 8], eax
	mov eax, [esi + 8]
	mov [TT + 12], eax
	mov eax, [esi + 20]
	mov [TT + 16], eax
	mov eax, [esi + 16]
	mov [TT + 20], eax
	lea esi, [esp + TTOFF + edx*4]
	mov eax, [esi]
	mov ebx, [esi + 4]
	shld eax, ebx, cl
	mov [L_D1], eax             ; s3
	mov eax, [esi + 8]
	shld ebx, eax, cl
	mov [L_D0], ebx             ; s2
	mov ebx, [esi + 12]
	shld eax, ebx, cl
	mov [V_S1], eax             ; s1
	mov eax, [esi + 16]
	shld ebx, eax, cl
	mov [V_S0], ebx             ; s0
	INVERT_PI1                  ; v of (s3 s2)

	mov eax, [VA_LA]
	sub eax, [VA_LB]
	inc eax
%if %2
	inc eax                     ; and the guard word
%endif
	mov [V_STEPS], eax          ; lenA - lenB + 1 quotient words
	mov eax, [VA_Q]
	mov [V_QP], eax
	mov eax, [VA_LB]
	mov [V_LB], eax
	mov ebp, [VA_W]
	add ebp, 8                  ; W: the first window (U[0])

.step:
	; TT = the top 8 digits of W, then T[k] = shld(TT[k + a], TT[k + a + 1], b) in place
	mov eax, [ebp + 4]
	mov [TT], eax
	mov eax, [ebp]
	mov [TT + 4], eax
	mov eax, [ebp + 12]
	mov [TT + 8], eax
	mov eax, [ebp + 8]
	mov [TT + 12], eax
	mov eax, [ebp + 20]
	mov [TT + 16], eax
	mov eax, [ebp + 16]
	mov [TT + 20], eax
	mov eax, [ebp + 28]
	mov [TT + 24], eax
	mov eax, [ebp + 24]
	mov [TT + 28], eax
	mov ecx, [V_B]
	mov esi, [V_A]
	lea esi, [esp + TTOFF + esi*4]
%assign k 0
%rep 6
	mov eax, [esi + 4*k]
	mov edx, [esi + 4*k + 4]
	shld eax, edx, cl
	mov [TT + 4*k], eax
%assign k k + 1
%endrep

%if %2
	; the top 4 digits of T == S: q^ = 2^64 - 1; above S: saturated (the left-out error)
	mov eax, [TT]
	cmp eax, [L_D1]
	jb .mini
	ja .saturate
	mov eax, [TT + 4]
	cmp eax, [L_D0]
	jb .mini
	ja .saturate
	mov eax, [TT + 8]
	cmp eax, [V_S1]
	jb .mini
	ja .saturate
	mov eax, [TT + 12]
	cmp eax, [V_S0]
	jb .mini
	ja .saturate
%else
	; the top 4 digits of T == S: q^ = 2^64 - 1
	mov eax, [TT]
	cmp eax, [L_D1]
	jne .mini
	mov eax, [TT + 4]
	cmp eax, [L_D0]
	jne .mini
	mov eax, [TT + 8]
	cmp eax, [V_S1]
	jne .mini
	mov eax, [TT + 12]
	cmp eax, [V_S0]
	jne .mini
%endif
	mov dword [QLO], -1
	mov dword [QHI], -1
	jmp .submul
.mini:
	MINISTEP 0
	mov [QHI], ebx
	MINISTEP 4
	mov [QLO], ebx
	or ebx, [QHI]
	jz .stored                  ; q^ = 0: W stays

.submul:                        ; W -= q^ * B, from the low end (as the q·D pass of Div.asm)
	mov edi, [VA_B]
	mov esi, [V_LB]
%if %2
	mov eax, [V_STEPS]
	add eax, 4                  ; the words of B kept: min(lenB, steps left + 4)
	cmp esi, eax
	jbe .submulCount
	mov esi, eax
.submulCount:
%endif
	dec esi
.submulLoop:
	mov eax, [edi + esi*8]      ; B[i] * q^ = P3:P2:P1:P0 (P2:P1 in ecx:ebx)
	mul dword [QLO]
	mov [P0], eax
	mov ebx, edx
	mov eax, [edi + esi*8]
	mul dword [QHI]
	add ebx, eax
	mov ecx, edx
	adc ecx, 0
	mov eax, [edi + esi*8 + 4]
	mul dword [QLO]
	add ebx, eax
	adc ecx, edx
	mov dword [P3], 0
	adc dword [P3], 0
	mov eax, [edi + esi*8 + 4]
	mul dword [QHI]
	add ecx, eax
	adc [P3], edx

	mov eax, [P0]
	sub [ebp + esi*8 + 8], eax  ; W[i + 1] -= the low half
	sbb [ebp + esi*8 + 12], ebx
	mov eax, [P3]
	sbb [ebp + esi*8], ecx      ; W[i] -= the high half
	sbb [ebp + esi*8 + 4], eax
	sbb dword [ebp + esi*8 - 8], 0   ; W[i - 1] -= borrow (W[-1]: the guard)
	sbb dword [ebp + esi*8 - 4], 0
	jc .borrow                  ; rare: the borrow runs further
.borrowDone:
	dec esi
	jns .submulLoop

	mov eax, [ebp]
	or eax, [ebp + 4]
%if %2
	jz .stored
	test dword [ebp + 4], 80000000h
	jnz .addBack                ; W < 0 (rare): its top word is beta - 1 or - 2
	jmp .saturate               ; a small top word: the window reached B (the left-out error)
%else
	jnz .addBack                ; W < 0 (rare): its top word is not 0
%endif
.stored:
	mov edx, [V_QP]
	mov eax, [QLO]
	mov [edx], eax
	mov eax, [QHI]
	mov [edx + 4], eax
	add edx, 8
	mov [V_QP], edx
	add ebp, 8                  ; the next window
	dec dword [V_STEPS]
	jnz .step
%if %2
	xor eax, eax                ; 0: Q' within 1
.done:
	xor edx, edx                ; (the result is u64: edx:eax)
%else

	; the remainder: the low lenB words of the last window (at ebp now) -> R[2 ..]
	mov edx, [VA_R]
	xor eax, eax
	mov [edx], eax
	mov [edx + 4], eax
	mov [edx + 8], eax
	mov [edx + 12], eax
	mov ecx, [V_LB]
	xor esi, esi
.copyR:
	mov eax, [ebp + esi*8]
	mov [edx + esi*8 + 16], eax
	mov eax, [ebp + esi*8 + 4]
	mov [edx + esi*8 + 20], eax
	inc esi
	cmp esi, ecx
	jb .copyR
%endif

	add esp, FRV
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

;------------------------------------------------------------------------------
; cold paths
;------------------------------------------------------------------------------
.borrow:                        ; the borrow out of W[i - 1]: on up (past the guard: dropped)
	lea eax, [esi - 2]
.borrowLoop:
	cmp eax, -1
	jl .borrowDone
	sub dword [ebp + eax*8], 1
	sbb dword [ebp + eax*8 + 4], 0
	jnc .borrowDone
	dec eax
	jmp .borrowLoop

%if %2
.saturate:                      ; the rest of Q' (this word on) is beta - 1
	mov edx, [V_QP]
	mov ecx, [V_STEPS]
.saturateLoop:
	mov dword [edx], -1
	mov dword [edx + 4], -1
	add edx, 8
	dec ecx
	jnz .saturateLoop
	mov eax, 1                  ; 1: saturated
	jmp .done
%endif

.addBack:                       ; q^ - 1, W += B, until the top word of W is 0
	sub dword [QLO], 1
	sbb dword [QHI], 0
	mov edi, [VA_B]
	mov esi, [V_LB]
%if %2
	mov eax, [V_STEPS]
	add eax, 4                  ; the same words of B as the pass
	cmp esi, eax
	jbe .addBackCount
	mov esi, eax
.addBackCount:
%endif
	xor eax, eax                ; CF = 0
.addBackLoop:
	mov eax, [edi + esi*8 - 8]
	adc [ebp + esi*8], eax
	mov eax, [edi + esi*8 - 4]
	adc [ebp + esi*8 + 4], eax
	dec esi
	jnz .addBackLoop
	adc dword [ebp], 0
	adc dword [ebp + 4], 0
	mov eax, [ebp]
	or eax, [ebp + 4]
	jnz .addBack
	jmp .stored
%endmacro

DIV_RECIP_V2 Fullint64DivWIN_recip_v2, 0
DIV_RECIP_V2 Fullint64DivQWIN_recip, 1
