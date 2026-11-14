
; =============================================================================
;  x86 (32-bit) port of x64/Div2Long.asm: A / d for a 64-bit d, one quotient word per
;  dividend word (leading zeros included), the remainder runs on to the next word.
;  Q may alias A (each word is read before it is written). cdecl.
;
;  ulong Fullint64Div2longWin(A, ulong d, Q, ulong* r, int lenA)   (the return value is unused)
;
;  x64: rdx:rax / rsi with one `div r64` per word. x86:
;    d < 2^32 : the same loop with two `div r32` per word (high dword, then low dword)
;    otherwise: fi_div128 per word (Div128.asm)
;
;  Register map: rdi A -> esi, rcx Q -> edi, r8 words left -> ebp, rsi d -> ebx (low dword;
;  the high dword stays in the arguments), rdx remainder -> edx (narrow path) or the
;  128-bit window on the stack (wide path).
; =============================================================================

section .text

extern fi_div128

global Fullint64Div2longWin
Fullint64Div2longWin:
	push ebp
	push ebx
	push esi
	push edi
	; arguments: [esp+20] A, +24 d low, +28 d high, +32 Q, +36 r, +40 lenA
	mov esi, [esp+20]
	mov edi, [esp+32]
	mov ebp, [esp+40]
	mov ebx, [esp+24]
	cmp dword [esp+28], 0
	jne .wide

	xor edx, edx                  ; remainder
.narrow:
	mov eax, [esi + 4]            ; high dword
	div ebx
	mov ecx, [esi]                ; low dword, read before the high quotient dword is stored
	mov [edi + 4], eax
	mov eax, ecx
	div ebx
	mov [edi], eax
	add esi, 8
	add edi, 8
	dec ebp
	jnz .narrow
	mov eax, [esp+36]
	mov [eax], edx                ; remainder
	mov dword [eax + 4], 0
	jmp .done

.wide:
	sub esp, 16                   ; window u0 u1 u2 u3: u2:u3 = remainder, u0:u1 = next word
	xor eax, eax
	mov [esp+8], eax
	mov [esp+12], eax
.wideLoop:
	mov eax, [esi]
	mov [esp], eax
	mov eax, [esi + 4]
	mov [esp+4], eax
	push dword [esp+16+28]        ; d high
	push ebx                      ; d low
	lea eax, [esp+8]
	push eax                      ; -> window
	call fi_div128
	add esp, 12
	mov [edi], eax                ; quotient word
	mov [edi + 4], edx
	mov eax, [esp]                ; the remainder moves up for the next word
	mov [esp+8], eax
	mov eax, [esp+4]
	mov [esp+12], eax
	add esi, 8
	add edi, 8
	dec ebp
	jnz .wideLoop
	mov ecx, [esp+16+36]
	mov eax, [esp+8]
	mov [ecx], eax                ; remainder
	mov eax, [esp+12]
	mov [ecx + 4], eax
	add esp, 16

.done:
	xor eax, eax
	xor edx, edx
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
