
; =============================================================================
;  x86 (32-bit) port of x64/ToDecString.asm. cdecl.
;
;  void Fullint64ToDecStringWin(value, valueBuf, resultBuf, int lenValue, ref int lenResult)
;    lenResult: in = the result buffer length, out = the start of the chars
;
;  Same algorithm: copy the value; each pass divides it in place by 10^19 word by word
;  (dropping the top word once it becomes zero), the remainder gives up to 19 chars from the
;  end of the buffer (zero-padded between chunks); stop when the value is zero.
;    div r64 by 10^19 -> fi_div128 (Div128.asm)
;    div r64 by 10    -> two div r32 (high dword, then low dword)
;
;  Register map (x64 -> x86)
;    rsi temp value item -> esi     rcx result buffer -> edi     rdi value length -> ebp
;    r9 words left -> LEFT          r10 result index -> RIDX     rbx chars left -> DCNT
;    rdx:rax remainder -> WIN[2..3] in the pass, ebx (high) + TLO (low) in the char phase
; =============================================================================

section .text

extern fi_div128

%define WIN    esp            ; 4 dwords for fi_div128
%define RIDX   esp+16
%define LEFT   esp+20
%define DCNT   esp+24
%define TLO    esp+28
%define FRAME  32

global Fullint64ToDecStringWin
Fullint64ToDecStringWin:
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
	mov ebp, ecx                  ; value len
	mov edi, [esp+FRAME+28]       ; result buffer
	mov eax, [esp+FRAME+36]
	mov eax, [eax]
	mov [RIDX], eax               ; result len value (ref int)

.mainLoop:
	xor eax, eax
	mov [WIN + 8], eax            ; remainder = 0
	mov [WIN + 12], eax
	mov [LEFT], ebp
.div2longLoop:
	;exif if it's the end of dividend
	cmp dword [LEFT], 0
	je .div2longDone

	;read next word, (remainder:word) / 10^19
	mov eax, [esi]
	mov [WIN], eax
	mov eax, [esi + 4]
	mov [WIN + 4], eax
	push 8AC72304h                ; 10^19 = 8AC72304_89E80000h
	push 89E80000h
	lea eax, [esp + 8]
	push eax
	call fi_div128
	add esp, 12
	mov [esi], eax                ; quotient word
	mov [esi + 4], edx
	mov ecx, [WIN]                ; the remainder moves up for the next word
	mov [WIN + 8], ecx
	mov ecx, [WIN + 4]
	mov [WIN + 12], ecx
	add esi, 8

	or eax, edx
	jnz .storeDiv
	cmp ebp, [LEFT]
	jne .storeDiv
	dec ebp                       ; decrement the result len for the next iterations
.storeDiv:
	dec dword [LEFT]
	jmp .div2longLoop

.div2longDone:
	;restore addres to the normal start of the rezult buff
	lea eax, [ebp*8]
	sub esi, eax

	mov eax, [WIN + 8]            ; the chunk (< 10^19)
	mov [TLO], eax
	mov ebx, [WIN + 12]
	mov dword [DCNT], 19
	mov ecx, 10
.charLoop:
	dec dword [DCNT]
	dec dword [RIDX]
	js .done

	xor edx, edx                  ; chunk / 10
	mov eax, ebx
	div ecx
	mov ebx, eax
	mov eax, [TLO]
	div ecx
	mov [TLO], eax

	;convert value 0 - 9 to char and store to the rez buff
	add dl, 30h
	mov eax, [RIDX]
	mov [edi + eax], dl

	mov eax, [TLO]
	or eax, ebx
	jnz .charLoop

	;compare the temp value with zero and break if zero
	cmp ebp, 1                    ; compare by len (normilazed value)
	ja .continue                  ;len more then 1
	jb .done                      ; len zero
	mov eax, [esi]
	or eax, [esi + 4]
	jz .done                      ;the temp value is zero

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
	mov [eax], ecx                ;return the start position of the result chars (ref int)
	add esp, FRAME
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
