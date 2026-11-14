
; =============================================================================
;  x86 (32-bit) port of x64/Shift.asm: magnitude shifts.
;  Limb order: index 0 = most significant word. Signs (and floor rounding of
;  negative >>) are handled by the caller.
;
;  shift = ws * 64 + s     (ws whole words, s = 0..63 bits)
;
;  Shl: R = A << shift,  lenR >= lenA + ws + 1
;       R = [lead zeros][A shifted: lenA + 1 words][ws zeros]
;  Shr: R = A >> shift,  lenR >= max(lenA - ws, 1);  R may alias A
;       R = [lead zeros][A[0 .. lenA-ws-1] shifted right by s]
;
;  void Fullint64ShlWIN(A, R, int lenA, int lenR, int shift)   (cdecl)
;
;  The x64 step out = shld(prev, cur, s) on 64-bit words becomes two 32-bit shld/shrd
;  per limb (hi/lo = the dwords at +4 / +0). x86 takes 32-bit shift counts mod 32, so
;  cl = s serves both cases; only which halves are paired changes:
;    s <  32:  outHi = shld(prevHi, prevLo)   outLo = shld(prevLo, curHi)
;    s >= 32:  outHi = shld(prevLo, curHi)    outLo = shld(curHi, curLo)
;
;  Register map: esi A (walks up), edi R (walks up), ecx shift (cl), ebx prevHi,
;  ebp prevLo, edx curHi, eax curLo; the limb counter and ws are on the stack.
; =============================================================================

section .text

global Fullint64ShlWIN
Fullint64ShlWIN:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 8                    ; [esp] limbs left, [esp+4] ws
	; arguments: [esp+28] A, +32 R, +36 lenA, +40 lenR, +44 shift
	mov esi, [esp+28]
	mov edi, [esp+32]
	mov eax, [esp+36]
	mov [esp], eax                ; lenA
	mov ecx, [esp+44]
	mov edx, ecx
	shr edx, 6
	mov [esp+4], edx              ; ws
	and ecx, 63                   ; s

	; lead = lenR - (lenA + 1 + ws)
	mov ebx, [esp+40]
	sub ebx, eax
	sub ebx, edx
	dec ebx
	xor eax, eax
.lead:
	test ebx, ebx
	jle .shift
	mov [edi], eax
	mov [edi + 4], eax
	add edi, 8
	dec ebx
	jmp .lead

.shift:
	xor ebx, ebx                  ; prev = 0 at the top
	xor ebp, ebp
	cmp ecx, 32
	jae .shiftHigh

.shiftLow:                        ; s < 32
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	shld ebx, ebp, cl             ; outHi
	mov [edi + 4], ebx
	shld ebp, edx, cl             ; outLo
	mov [edi], ebp
	mov ebx, edx                  ; prev = cur
	mov ebp, eax
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .shiftLow
	shld ebx, ebp, cl             ; lowest word: prev << s
	mov [edi + 4], ebx
	shl ebp, cl
	mov [edi], ebp
	add edi, 8
	jmp .tail

.shiftHigh:                       ; s >= 32 (cl acts as s - 32)
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	shld ebp, edx, cl             ; outHi = (prevLo, curHi)
	mov [edi + 4], ebp
	shld edx, eax, cl             ; outLo = (curHi, curLo)
	mov [edi], edx
	mov ebp, eax                  ; prevLo = curLo (prevHi is not used here)
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .shiftHigh
	shl ebp, cl                   ; lowest word: prev << s
	mov [edi + 4], ebp
	mov dword [edi], 0
	add edi, 8

.tail:                            ; ws zero words
	mov ebx, [esp+4]
	xor eax, eax
.tailLoop:
	test ebx, ebx
	jz .done
	mov [edi], eax
	mov [edi + 4], eax
	add edi, 8
	dec ebx
	jmp .tailLoop
.done:
	add esp, 8
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

;------------------------------------------------------------------------------
;  Shr, mirrored with shrd (prev = the more significant word):
;    s <  32:  outLo = shrd(curLo, curHi)     outHi = shrd(curHi, prevLo)
;    s >= 32:  outLo = shrd(curHi, prevLo)    outHi = shrd(prevLo, prevHi)
;  Each limb is read before it is written, so R may alias A.
;------------------------------------------------------------------------------
global Fullint64ShrWIN
Fullint64ShrWIN:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 4                    ; [esp] source limbs left
	; arguments: [esp+24] A, +28 R, +32 lenA, +36 lenR, +40 shift
	mov esi, [esp+24]
	mov edi, [esp+28]
	mov ecx, [esp+40]
	mov edx, ecx
	shr edx, 6                    ; ws
	and ecx, 63                   ; s

	mov eax, [esp+32]
	sub eax, edx
	jbe .allZero                  ; lenA <= ws: everything shifted out
	mov [esp], eax                ; count = lenA - ws source words

	mov ebx, [esp+36]
	sub ebx, eax                  ; lead = lenR - count
	xor eax, eax
.lead:
	test ebx, ebx
	jle .shift
	mov [edi], eax
	mov [edi + 4], eax
	add edi, 8
	dec ebx
	jmp .lead

.shift:
	xor ebx, ebx                  ; prev = 0 at the top
	xor ebp, ebp
	cmp ecx, 32
	jae .shiftHigh

.shiftLow:                        ; s < 32
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	mov ebx, eax                  ; keep curLo for prev (prevHi is not used here)
	shrd eax, edx, cl             ; outLo
	mov [edi], eax
	shrd edx, ebp, cl             ; outHi
	mov [edi + 4], edx
	mov ebp, ebx                  ; prevLo = curLo
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .shiftLow
	jmp .done

.shiftHigh:                       ; s >= 32 (cl acts as s - 32)
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	push edx                      ; curHi, the next prevHi
	shrd edx, ebp, cl             ; outLo = (curHi, prevLo)
	mov [edi], edx
	shrd ebp, ebx, cl             ; outHi = (prevLo, prevHi)
	mov [edi + 4], ebp
	mov ebp, eax                  ; prev = cur
	pop ebx
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .shiftHigh
	jmp .done

.allZero:
	mov ebx, [esp+36]             ; lenR
	xor eax, eax
.zero:
	mov [edi], eax
	mov [edi + 4], eax
	add edi, 8
	dec ebx
	jnz .zero

.done:
	add esp, 4
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
