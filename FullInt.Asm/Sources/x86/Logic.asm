
; =============================================================================
;  x86 (32-bit) port of x64/Logic.asm: bitwise AND / OR / XOR with BigInteger
;  semantics (two's complement view of negative numbers) on sign-magnitude operands.
;  Limb order: index 0 = most significant word (same as the rest of FullInt).
;
;  A, B   magnitudes, lenA / lenB words (>= 1)
;  signs  bit0 = A is negative, bit1 = B is negative
;  R      lenR words, lenR >= max(lenA, lenB) + 1; receives the result magnitude
;  return eax = 1 if the result is negative, 0 otherwise
;
;  int Fullint64AndWIN(A, B, R, int lenA, int lenB, int lenR, int signs)   (cdecl)
;
;  Same single pass from LSW to MSW as x64; every 32-bit half goes through
;      t = (dword XOR mask) + carry          mask = 0 or all-ones, carry starts at mask & 1
;  and the carries run on through both halves of a limb and on to the next limb.
;
;  Register map (x64 -> x86)
;    rdi A, rsi B          -> esi, edi   (bases; the current limb is [base + left*8 - 8])
;    rdx A left, r8 B left -> ebx, ecx
;    rcx R, r9 R left      -> ebp (walks down from the end of R), stack [esp+24]
;    rbx maskA, r12 carryA -> stack [esp], [esp+4] (byte: NOT carryA)
;    r13 maskB, r14 carryB -> stack [esp+8], [esp+12] (byte: NOT carryB)
;    r15 maskR, r10 carryR -> stack [esp+16], [esp+20] (byte: NOT carryR)
;    rax / r11 A / B word  -> eax / edx (one dword at a time)
; =============================================================================

section .text

; one 32-bit half (%3 = 0 low, 4 high) of the current limb, %2 = and / or / xor
;  The carries live on the stack as one byte holding NOT carry, read back as a byte:
;  cmp byte [nc], 1 sets CF = carry, then adc / setnc. (A byte store read back as a dword
;  cannot be forwarded and stalled every half on all three chains.)
%macro HALF 3
	xor eax, eax
	test ebx, ebx
	jz %%noA
	mov eax, [esi + ebx*8 - 8 + %3]
%%noA:
	xor eax, [esp]                ; A dword -> two's complement
	cmp byte [esp+4], 1           ; CF = carryA
	adc eax, 0
	setnc byte [esp+4]

	xor edx, edx
	test ecx, ecx
	jz %%noB
	mov edx, [edi + ecx*8 - 8 + %3]
%%noB:
	xor edx, [esp+8]              ; B dword -> two's complement
	cmp byte [esp+12], 1          ; CF = carryB
	adc edx, 0
	setnc byte [esp+12]

	%2 eax, edx

	xor eax, [esp+16]             ; result -> magnitude
	cmp byte [esp+20], 1          ; CF = carryR
	adc eax, 0
	setnc byte [esp+20]
	mov [ebp + %3], eax
%endmacro

%macro BITWISE_OP 2 ; %1 = name suffix, %2 = instruction (and / or / xor)

global Fullint64%1WIN
Fullint64%1WIN:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 28                   ; locals, see the register map
	; arguments: [esp+48] A, +52 B, +56 R, +60 lenA, +64 lenB, +68 lenR, +72 signs
	mov esi, [esp+48]
	mov edi, [esp+52]
	mov ebx, [esp+60]
	mov ecx, [esp+64]
	mov eax, [esp+68]
	mov [esp+24], eax             ; R words left
	mov ebp, [esp+56]
	lea ebp, [ebp + eax*8]        ; end of R

	mov eax, [esp+72]             ; signs
	mov edx, eax
	and edx, 1
	neg edx
	mov [esp], edx                ; maskA = signA ? all-ones : 0
	inc edx
	mov [esp+4], edx              ; NOT carryA (carryA = signA)
	shr eax, 1
	and eax, 1
	neg eax
	mov [esp+8], eax              ; maskB
	lea edx, [eax + 1]
	mov [esp+12], edx             ; NOT carryB
	mov edx, [esp]
	%2 edx, eax                   ; maskR = op(maskA, maskB)
	mov [esp+16], edx
	inc edx
	mov [esp+20], edx             ; NOT carryR (carryR = signR)

%%loop:
	sub ebp, 8                    ; R limb, from the lowest up
	HALF %1, %2, 0
	HALF %1, %2, 4
	test ebx, ebx                 ; next A / B limb (they stay at 0 above the top)
	jz %%aDone
	dec ebx
%%aDone:
	test ecx, ecx
	jz %%bDone
	dec ecx
%%bDone:
	dec dword [esp+24]
	jnz %%loop

	mov eax, [esp+16]
	and eax, 1                    ; result sign

	add esp, 28
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
%endmacro

BITWISE_OP And, and
BITWISE_OP Or,  or
BITWISE_OP Xor, xor
