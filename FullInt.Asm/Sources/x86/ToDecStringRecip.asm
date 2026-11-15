; =============================================================================
;  x86 (32-bit) ToDecString with the division by 10^19 through its reciprocal, next to
;  ToDecString.asm (the baseline, kept as is): the same algorithm and contract
;  (Fullint64ToDecStringWin). cdecl. Plain i386: any CPU; picked unless FULLINT_CPU=baseline.
;
;  void Fullint64ToDecStringWIN_recip(value, valueBuf, resultBuf, int lenValue, ref int lenResult)
;
;  - Each 32-bit digit of the value: one 3-by-2 division by 10^19 = (d1 d0) with the constant
;    reciprocal v (10^19 has its top bit set: no normalization), which gives the exact
;    remainder too (was DIGIT10E19: div r32 and a correction loop).
;  - A 19-digit chunk: chunk = a * 10^18 + b * 10^9 + c by three div r32, then b and c
;    (< 10^9) digit by digit at once - two independent chains, x / 10 = (x * 0CCCCCCCDh) >> 35
;    (was two div r32 per digit, 38 per chunk). The top chunk is written whole too and its
;    leading zeros skipped (written below the start index, which the callers do not read).
;    A result buffer with fewer than 19 places left: the original digit loop.
;
;  Register map as in ToDecString.asm; the division pass: esi value, ebp:ecx remainder,
;  edi next digit, ebx quotient digit.
; =============================================================================

section .text

%define RIDX   esp
%define LEFT   esp+4
%define DCNT   esp+8
%define LEN    esp+12
%define TEN    esp+16
%define Q0     esp+20
%define SAVESI esp+24
%define FRAME  28

%define D_HI   8AC72304h          ; 10^19 = 8AC72304_89E80000h
%define D_LO   89E80000h
%define V_REC  0D83C94FBh         ; floor((2^96 - 1) / 10^19) - 2^32

; (ebp ecx edi) / 10^19 -> ebx, the remainder in ebp:ecx (ebp:ecx < 10^19 on entry)
%macro RECIP3BY2 0
	mov eax, V_REC
	mul ebp
	add eax, ecx
	adc edx, ebp                ; (q : q0) = v n2 + (n2 : n1)
	mov ebx, edx
	mov [Q0], eax
	imul eax, ebx, D_HI
	sub ecx, eax                ; r1 = n1 - d1 q
	sub edi, D_LO
	sbb ecx, D_HI               ; (r1 : r0) = (r1 : n0) - (d1 : d0)
	mov eax, D_LO
	mul ebx
	sub edi, eax
	sbb ecx, edx                ; -= d0 q
	inc ebx
	cmp ecx, [Q0]
	sbb eax, eax
	not eax                     ; mask = r1 >= q0
	add ebx, eax
	mov edx, eax
	and eax, D_LO
	and edx, D_HI
	add edi, eax
	adc ecx, edx
	cmp ecx, D_HI
	jb %%done
	ja %%inc
	cmp edi, D_LO
	jb %%done
%%inc:                          ; rare
	inc ebx
	sub edi, D_LO
	sbb ecx, D_HI
%%done:
	mov ebp, ecx
	mov ecx, edi
%endmacro

global Fullint64ToDecStringWIN_recip
Fullint64ToDecStringWIN_recip:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRAME
	; arguments: [esp+FRAME+20] value, +24 valueBuf, +28 resultBuf, +32 lenValue, +36 &lenResult
	mov esi, [esp+FRAME+20]
	mov edi, [esp+FRAME+24]
	mov ecx, [esp+FRAME+32]
	xor ebx, ebx
.copyLoop:
	mov eax, [esi + ebx*8]
	mov [edi + ebx*8], eax
	mov eax, [esi + ebx*8 + 4]
	mov [edi + ebx*8 + 4], eax
	inc ebx
	cmp ebx, ecx
	jne .copyLoop

	mov esi, edi                  ; the temp value
	mov [LEN], ecx                ; value len
	mov eax, [esp+FRAME+36]
	mov eax, [eax]
	mov [RIDX], eax               ; result len value (ref int)
	mov dword [TEN], 10

.mainLoop:
	xor ebp, ebp                  ; remainder = 0
	xor ecx, ecx
	mov eax, [LEN]
	mov [LEFT], eax
.div2longLoop:
	cmp dword [LEFT], 0
	je .div2longDone

	; (remainder:word) / 10^19: two 32-bit digits, the high one first
	mov edi, [esi + 4]
	RECIP3BY2
	mov [esi + 4], ebx            ; quotient high dword
	mov edi, [esi]
	RECIP3BY2
	mov [esi], ebx                ; quotient low dword
	add esi, 8

	or ebx, [esi - 4]
	jnz .storeDiv
	mov eax, [LEN]
	cmp eax, [LEFT]
	jne .storeDiv
	dec dword [LEN]               ; the top word became zero
.storeDiv:
	dec dword [LEFT]
	jmp .div2longLoop

.div2longDone:
	mov eax, [LEN]
	shl eax, 3
	sub esi, eax                  ; back to the start of the value

	; the chunk (< 10^19) is ebp:ecx
	cmp dword [RIDX], 19
	jl .slowDigits
	mov [SAVESI], esi
	mov edi, 1000000000
	xor edx, edx
	mov eax, ebp
	div edi
	mov ebx, eax                  ; (chunk / 10^9) high dword
	mov eax, ecx
	div edi                       ; eax: its low dword, edx = c
	mov ecx, edx
	mov edx, ebx
	div edi                       ; eax = a (< 10), edx = b
	mov ebx, edx
	mov edi, [esp+FRAME+28]
	add edi, [RIDX]               ; one past the chunk's lowest digit
	add al, 30h
	mov [edi - 19], al
	mov esi, 0CCCCCCCDh
%assign i 0
%rep 9
	mov eax, ecx                  ; c
	mul esi
	shr edx, 3
	lea eax, [edx + edx*4]
	add eax, eax
	sub ecx, eax
	add cl, 30h
	mov [edi - 1 - i], cl
	mov ecx, edx
	mov eax, ebx                  ; b
	mul esi
	shr edx, 3
	lea eax, [edx + edx*4]
	add eax, eax
	sub ebx, eax
	add bl, 30h
	mov [edi - 10 - i], bl
	mov ebx, edx
%assign i i + 1
%endrep
	sub dword [RIDX], 19
	mov esi, [SAVESI]

	; the top chunk (the value is zero now)? skip its leading zeros, keep one digit
	cmp dword [LEN], 1
	ja .mainLoop
	jb .trim
	mov eax, [esi]
	or eax, [esi + 4]
	jnz .mainLoop
.trim:
	mov edi, [esp+FRAME+28]
	mov eax, [RIDX]
	lea edx, [eax + 18]
.trimLoop:
	cmp eax, edx
	jae .trimmed
	cmp byte [edi + eax], 30h
	jne .trimmed
	inc eax
	jmp .trimLoop
.trimmed:
	mov [RIDX], eax
	jmp .done

	; ---- the original digit loop (fewer than 19 places left in the result buffer) ----
.slowDigits:
	mov edi, [esp+FRAME+28]       ; result buffer
	mov dword [DCNT], 19
.charLoop:
	dec dword [DCNT]
	dec dword [RIDX]
	js .done

	xor edx, edx                  ; chunk / 10: high dword, then low dword
	mov eax, ebp
	div dword [TEN]
	mov ebp, eax
	mov eax, ecx
	div dword [TEN]
	mov ecx, eax

	add dl, 30h
	mov ebx, [RIDX]
	mov [edi + ebx], dl

	or eax, ebp
	jnz .charLoop

	cmp dword [LEN], 1
	ja .continue
	jb .done
	mov eax, [esi]
	or eax, [esi + 4]
	jz .done

.continue:
.storeZeroChars:
	cmp dword [DCNT], 0
	jle .mainLoop
	dec dword [RIDX]
	js .done
	mov eax, [RIDX]
	mov byte [edi + eax], 30h
	dec dword [DCNT]
	jmp .storeZeroChars

.done:
	mov eax, [esp+FRAME+36]
	mov ecx, [RIDX]
	mov [eax], ecx                ; the start position of the result chars (ref int)
	add esp, FRAME
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
