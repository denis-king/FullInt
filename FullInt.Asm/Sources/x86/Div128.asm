
; =============================================================================
;  fi_div128: 128 / 64 -> 64-bit quotient with 32-bit instructions.
;  The x64 kernels use one `div r64` (rdx:rax / r64) here; x86 has only `div r32`, so this
;  is that one instruction, built the textbook way (Knuth D with a two-digit divisor,
;  32-bit digits). It is the same operation as Div128 in the managed core.
;
;  in : [esp+4]  p -> u, 4 dwords, lowest first: u0 u1 u2 u3 (the 128-bit dividend)
;       [esp+8]  d low dword, [esp+12] d high dword   (d != 0)
;       requires u3:u2 < d (the quotient fits 64 bits), like `div r64` requires rdx < divisor
;  out: edx:eax quotient; the remainder (< d) replaces u0:u1, u2 and u3 become 0
;  internal (not exported), cdecl; saves ebx, esi, edi, ebp
; =============================================================================

section .text

global fi_div128
fi_div128:
	push ebp
	push ebx
	push esi
	push edi
	mov esi, [esp+20]             ; u
	mov ebx, [esp+24]             ; d low
	mov ecx, [esp+28]             ; d high
	test ecx, ecx
	jnz .twoDigits

	; d < 2^32: then u3 = 0 and u2 < d, two div r32
	mov edx, [esi+8]
	mov eax, [esi+4]
	div ebx                       ; high quotient dword
	mov edi, eax
	mov eax, [esi]
	div ebx                       ; low quotient dword, edx = remainder
	mov [esi], edx
	xor edx, edx
	mov [esi+4], edx
	mov [esi+8], edx
	mov [esi+12], edx
	mov edx, edi
	jmp .return

.twoDigits:
	sub esp, 28
	; locals: [esp] un0, [esp+4] un1, [esp+8] un2, [esp+12] un3, [esp+16] v0, [esp+20] v1, [esp+24] q1
	; normalize: s = leading zeros of d high; v = d << s, un = u << s
	; (u < d * 2^64, so u << s still fits 128 bits)
	bsr edi, ecx
	neg edi
	add edi, 31
	mov eax, ecx                  ; keep s in edi, cl gets it below
	mov ecx, edi
	shld eax, ebx, cl
	mov [esp+20], eax             ; v1 (top bit set)
	shl ebx, cl
	mov [esp+16], ebx             ; v0
	mov eax, [esi+12]
	mov edx, [esi+8]
	shld eax, edx, cl
	mov [esp+12], eax             ; un3
	mov eax, [esi+4]
	shld edx, eax, cl
	mov [esp+8], edx              ; un2
	mov edx, [esi]
	shld eax, edx, cl
	mov [esp+4], eax              ; un1
	shl edx, cl
	mov [esp], edx                ; un0
	push edi                      ; s (locals move by 4 below)

%define UN0 esp+4
%define UN1 esp+8
%define UN2 esp+12
%define UN3 esp+16
%define V0  esp+20
%define V1  esp+24
%define Q1  esp+28

; one quotient digit of (top:mid:low) / (v1:v0); qhat in edi; the window becomes the remainder
%macro DIGIT 3 ; %1 top, %2 mid, %3 low
	mov edx, [%1]
	mov eax, [%2]
	cmp edx, [V1]
	jae %%qmax                    ; top == v1: the estimate would not fit a digit
	div dword [V1]                ; eax = qhat, edx = rhat
	mov edi, eax
	mov ebp, edx
	jmp %%refine
%%qmax:
	mov edi, 0FFFFFFFFh
	mov ebp, [%2]
	add ebp, [V1]                 ; rhat = mid + v1
	jc %%mulsub                   ; rhat >= 2^32: the test below cannot fail
%%refine:                         ; while qhat * v0 > rhat:low: qhat--, rhat += v1
	mov eax, edi
	mul dword [V0]
	cmp edx, ebp
	jb %%mulsub
	ja %%dec
	cmp eax, [%3]
	jbe %%mulsub
%%dec:
	dec edi
	add ebp, [V1]
	jnc %%refine                  ; rhat still < 2^32: test again
%%mulsub:                         ; (top:mid:low) -= qhat * (v1:v0)
	mov eax, edi
	mul dword [V0]
	mov ebx, eax                  ; P0
	mov ecx, edx
	mov eax, edi
	mul dword [V1]
	add ecx, eax                  ; P1
	adc edx, 0                    ; P2
	sub [%3], ebx
	sbb [%2], ecx
	sbb [%1], edx
	jnc %%done
	dec edi                       ; one too large (rare): add the divisor back
	mov eax, [V0]
	add [%3], eax
	mov eax, [V1]
	adc [%2], eax
	adc dword [%1], 0
%%done:
%endmacro

	DIGIT UN3, UN2, UN1
	mov [Q1], edi
	DIGIT UN2, UN1, UN0

	; remainder = (un1:un0) >> s
	pop ecx                       ; s
	mov eax, [esp]                ; un0
	mov edx, [esp+4]              ; un1
	shrd eax, edx, cl
	shr edx, cl
	mov [esi], eax
	mov [esi+4], edx
	xor eax, eax
	mov [esi+8], eax
	mov [esi+12], eax
	mov eax, edi                  ; quotient low dword
	mov edx, [esp+24]             ; quotient high dword (q1)
	add esp, 28

%undef UN0
%undef UN1
%undef UN2
%undef UN3
%undef V0
%undef V1
%undef Q1

.return:
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
