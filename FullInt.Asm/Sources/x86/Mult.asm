
; =============================================================================
;  x86 (32-bit) port of x64/Mult.asm: the same column-wise (product scanning)
;  multiply over 64-bit words, the same column bounds and pair walk. cdecl.
;
;  ulong Fullint64MultWIN(A, B, R, int lenA, int lenB, int lenR)   (the return value is unused)
;
;  x86 has no 64x64 -> 128 multiply, so a pair of 64-bit words is four 32x32 products
;  (aLo·bLo, aLo·bHi, aHi·bLo, aHi·bHi), and the x64 accumulator r13:r14:r15 becomes three
;  96-bit sums per column, each fed without carries between them:
;    S0 = sum aLo·bLo           weight 1      (starts with the carry from the previous column)
;    S1 = sum aLo·bHi + aHi·bLo weight 2^32   (ebx:ecx:ebp - two products per pair)
;    S2 = sum aHi·bHi           weight 2^64
;  At the end of a column T = S0 + S1·2^32 + S2·2^64: the low 64 bits are the result word,
;  T >> 64 (< 2^95) is the next column's S0 - the x64 "store r13, r14 -> r13, r15 -> r14".
;
;  Register map (x64 -> x86)
;    r9  A item          -> esi           r11 B item        -> edi
;    r13:r14:r15 acc     -> S0 (stack) + S1 (ebx:ecx:ebp) + S2 (stack)
;    rbp end of A range  -> [esp+24]      r10 column A item -> [esp+28]
;    r12 R item          -> [esp+32]      rbx last A item   -> [esp+36]
;    r8  last B item     -> [esp+40]      rdi / rsi / rcx   -> arguments A / B / R on the stack
; =============================================================================

%include "abi86.inc"
section .text

ENTRY Fullint64MultWIN
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 44
	; locals: [esp] S0 (3 dwords), [esp+12] S2 (3 dwords), [esp+24] .. [esp+40] see the map
	; arguments: [esp+64] A, +68 B, +72 R, +76 lenA, +80 lenB, +84 lenR
	mov eax, [esp+64]
	mov edx, [esp+76]
	lea eax, [eax + edx*8 - 8]    ; the latest A position
	mov [esp+28], eax
	mov [esp+36], eax             ; copy the latest position
	mov eax, [esp+72]
	mov edx, [esp+84]
	lea eax, [eax + edx*8 - 8]    ; the latest R position
	mov [esp+32], eax
	mov eax, [esp+68]
	mov edx, [esp+80]
	lea eax, [eax + edx*8 - 8]    ; the latest B position
	mov [esp+40], eax

	xor eax, eax
	mov [esp], eax                ; S0 = 0
	mov [esp+4], eax
	mov [esp+8], eax
	mov [esp+12], eax             ; S2 = 0
	mov [esp+16], eax
	mov [esp+20], eax
	xor ebx, ebx                  ; S1 = 0
	xor ecx, ecx
	xor ebp, ebp

.outerIteration:
	; column bounds once per column, so the inner loop only walks real pairs:
	;   A from esi = max(colA, baseA) up to end = min(lastA, esi + (edi - baseB))
	;   B from edi = lastB - (esi - colA) down
	mov esi, [esp+28]             ; restore shifted A item with current A iter
	cmp esi, [esp+64]
	jae .aStart
	mov esi, [esp+64]             ; A item before A start -> start at A[0]
.aStart:
	mov edi, [esp+40]
	add edi, [esp+28]
	sub edi, esi                  ; B position paired with esi
	mov eax, edi
	sub eax, [esp+68]             ; bytes of B left below edi
	jb .innerDone                 ; no B words for this column (only when lenR > lenA + lenB)
	add eax, esi
	cmp eax, [esp+36]
	jbe .aEnd
	mov eax, [esp+36]             ; last A item of the column
.aEnd:
	mov [esp+24], eax

.innerIteration:                  ; one pair: four 32x32 products
	mov eax, [esi]
	mul dword [edi]               ; aLo·bLo -> S0
	add [esp], eax
	adc [esp+4], edx
	adc dword [esp+8], 0

	mov eax, [esi]
	mul dword [edi+4]             ; aLo·bHi -> S1
	add ebx, eax
	adc ecx, edx
	adc ebp, 0

	mov eax, [esi+4]
	mul dword [edi]               ; aHi·bLo -> S1
	add ebx, eax
	adc ecx, edx
	adc ebp, 0

	mov eax, [esi+4]
	mul dword [edi+4]             ; aHi·bHi -> S2
	add [esp+12], eax
	adc [esp+16], edx
	adc dword [esp+20], 0

	add esi, 8                    ; shift A index + B index
	sub edi, 8
	cmp esi, [esp+24]
	jbe .innerIteration

.innerDone:
	; T = S0 + U·2^32,  U = S1 + S2·2^32  (U0 .. U2 in ebx, ecx, ebp; U3 in eax)
	add ecx, [esp+12]             ; U1 = S1[1] + S2[0]
	adc ebp, [esp+16]             ; U2 = S1[2] + S2[1]
	mov eax, [esp+20]
	adc eax, 0                    ; U3 = S2[2] + carry
	mov edi, [esp+32]
	mov edx, [esp]
	mov [edi], edx                ; store the result: low dword  = S0[0]
	mov edx, [esp+4]
	add edx, ebx
	mov [edi+4], edx              ;                   high dword = S0[1] + U0
	mov edx, [esp+8]
	adc edx, ecx                  ; T >> 64 = the next column's S0
	adc ebp, 0
	adc eax, 0
	mov [esp], edx
	mov [esp+4], ebp
	mov [esp+8], eax
	xor ebx, ebx                  ; S1 = 0, S2 = 0
	xor ecx, ecx
	xor ebp, ebp
	mov [esp+12], ebx
	mov [esp+16], ebx
	mov [esp+20], ebx

	sub dword [esp+28], 8         ; move A index to the next position
	;outer loop result lenth
	mov eax, [esp+32]
	cmp eax, [esp+72]             ; compare the address with the base: equal -> done
	je .done
	sub dword [esp+32], 8
	jmp .outerIteration

.done:
	add esp, 44
	xor eax, eax
	xor edx, edx
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
