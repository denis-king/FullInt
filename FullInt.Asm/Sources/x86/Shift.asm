
; =============================================================================
;  x86 (32-bit) port of x64/Shift.asm: magnitude shifts.
;  Limb order: index 0 = most significant word. Signs (and floor rounding of
;  negative >>) are handled by the caller.
;
;  shift = ws * 64 + s     (ws whole words, s = 0..63 bits)
;
;  Shl: R = A << shift,  lenR >= lenA + ws + 1
;       R = [lead zeros][A shifted: lenA + 1 words][ws zeros]
;  Shr: R = A >> shift,  lenR >= max(lenA - ws, 1);  R may alias A (with ws >= 1 low end first)
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
;  The main loops (.fastLow / .fastHigh) compute the same with shl/shr/or, as x64 does:
;  after neg ecx, cl acts as 32 - (s mod 32). That needs s mod 32 > 0, so s = 0 and
;  s = 32 keep the shld loops (.shiftLow / .shiftHigh).
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
	test ecx, 31
	jz .shiftWhole                ; s = 0 or 32: the shld loops below
	cmp ecx, 32
	jae .fastHigh

	; as in x64: shl/shr/or instead of shld r, r, cl (several uops, slow on recent cores).
	; The shift unit takes a 32-bit count mod 32, so after neg ecx cl acts as 32 - (s mod 32);
	; s mod 32 > 0 here.
.fastLow:                         ; s < 32
	mov edx, [esi + 4]            ; curHi
	shl ebx, cl                   ; prevHi << s
	mov eax, ebp
	shl eax, cl                   ; prevLo << s
	neg ecx                       ; cl = 32 - s
	shr ebp, cl                   ; prevLo >> (32 - s)
	or ebx, ebp                   ; outHi
	mov ebp, edx
	shr ebp, cl                   ; curHi >> (32 - s)
	or eax, ebp                   ; outLo
	neg ecx                       ; cl = s
	mov [edi + 4], ebx
	mov [edi], eax
	mov ebx, edx                  ; prev = cur
	mov ebp, [esi]
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .fastLow
	jmp .lowestLow

.fastHigh:                        ; s >= 32 (cl acts as t = s - 32)
	mov eax, [esi + 4]            ; curHi
	mov edx, [esi]                ; curLo
	shl ebp, cl                   ; prevLo << t
	mov ebx, eax
	shl ebx, cl                   ; curHi << t
	neg ecx                       ; cl = 32 - t
	shr eax, cl                   ; curHi >> (32 - t)
	or ebp, eax                   ; outHi
	mov eax, edx
	shr eax, cl                   ; curLo >> (32 - t)
	or ebx, eax                   ; outLo
	neg ecx                       ; cl = t
	mov [edi + 4], ebp
	mov [edi], ebx
	mov ebp, edx                  ; prevLo = curLo
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .fastHigh
	jmp .lowestHigh

.shiftWhole:
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
.lowestLow:
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
.lowestHigh:
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

	; R over A with whole words shifted out (ws >= 1): this top-down pass would overwrite
	; limbs of A before reading them (the lead zeros, and every store ws limbs up) - that case
	; goes low end first (.inPlace). R apart from A, or ws = 0: as before.
	test edx, edx
	jz .topDown
	mov ebx, [esp+32]
	lea ebx, [esi + ebx*8]        ; the end of A
	cmp edi, ebx
	jae .topDown
	mov ebx, [esp+36]
	lea ebx, [edi + ebx*8]        ; the end of R
	cmp esi, ebx
	jb .inPlace
.topDown:
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
	test ecx, 31
	jz .shiftWhole                ; s = 0 or 32: the shrd loops below
	cmp ecx, 32
	jae .fastHigh

	; shl/shr/or as in Shl; both dwords of a limb are read before the limb is written,
	; so R may still alias A.
.fastLow:                         ; s < 32
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	neg ecx                       ; cl = 32 - s
	shl ebp, cl                   ; prevLo << (32 - s)
	mov ebx, edx
	shl ebx, cl                   ; curHi << (32 - s)
	neg ecx                       ; cl = s
	shr edx, cl                   ; curHi >> s
	or edx, ebp                   ; outHi
	mov ebp, eax                  ; prevLo = curLo
	shr eax, cl                   ; curLo >> s
	or eax, ebx                   ; outLo
	mov [edi], eax
	mov [edi + 4], edx
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .fastLow
	jmp .done

.fastHigh:                        ; s >= 32 (cl acts as t = s - 32)
	mov edx, [esi + 4]            ; curHi
	neg ecx                       ; cl = 32 - t
	shl ebx, cl                   ; prevHi << (32 - t)
	mov eax, ebp
	shl eax, cl                   ; prevLo << (32 - t)
	neg ecx                       ; cl = t
	shr ebp, cl                   ; prevLo >> t
	or ebx, ebp                   ; outHi
	mov ebp, edx
	shr ebp, cl                   ; curHi >> t
	or eax, ebp                   ; outLo
	mov ebp, [esi]                ; prevLo = curLo, read before the limb is written
	mov [edi], eax
	mov [edi + 4], ebx
	mov ebx, edx                  ; prevHi = curHi
	add esi, 8
	add edi, 8
	dec dword [esp]
	jnz .fastHigh
	jmp .done

.shiftWhole:
	cmp ecx, 32
	jae .shiftHigh

.shiftLow:                        ; s < 32
	mov eax, [esi]                ; curLo
	mov edx, [esi + 4]            ; curHi
	mov ebx, eax                 ; keep curLo for prev (prevHi is not used here)
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

.inPlace:                         ; from the low end, the lead zeros last (count in [esp], eax)
	mov ebx, [esp+36]
	sub ebx, eax                  ; lead
	mov [esp+36], ebx
	lea edi, [edi + ebx*8]        ; R[lead]
	lea esi, [esi + eax*8 - 8]    ; A[count-1]
	lea edi, [edi + eax*8 - 8]    ; its place
	dec dword [esp]               ; limbs with a limb above them
	jz .inPlaceTop
	cmp ecx, 32
	jae .inPlaceHigh
.inPlaceLow:                      ; s < 32: outLo = shrd(curLo, curHi), outHi = shrd(curHi, prevLo)
	mov eax, [esi]
	mov edx, [esi + 4]
	mov ebp, [esi - 8]
	shrd eax, edx, cl
	shrd edx, ebp, cl
	mov [edi], eax
	mov [edi + 4], edx
	sub esi, 8
	sub edi, 8
	dec dword [esp]
	jnz .inPlaceLow
	jmp .inPlaceTop
.inPlaceHigh:                     ; s >= 32 (cl acts as s - 32): outLo = shrd(curHi, prevLo), outHi = shrd(prevLo, prevHi)
	mov edx, [esi + 4]
	mov ebp, [esi - 8]
	mov ebx, [esi - 4]
	shrd edx, ebp, cl
	shrd ebp, ebx, cl
	mov [edi], edx
	mov [edi + 4], ebp
	sub esi, 8
	sub edi, 8
	dec dword [esp]
	jnz .inPlaceHigh
.inPlaceTop:                      ; k = 0: A[0] >> s
	mov eax, [esi]
	mov edx, [esi + 4]
	cmp ecx, 32
	jae .inPlaceTopHigh
	shrd eax, edx, cl
	shr edx, cl
	jmp .inPlaceTopStore
.inPlaceTopHigh:
	mov eax, edx
	shr eax, cl
	xor edx, edx
.inPlaceTopStore:
	mov [edi], eax
	mov [edi + 4], edx
	mov ebx, [esp+36]
	xor eax, eax
.inPlaceLead:
	test ebx, ebx
	jz .done
	sub edi, 8
	mov [edi], eax
	mov [edi + 4], eax
	dec ebx
	jmp .inPlaceLead

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
