
; =============================================================================
;  x86 (32-bit) port of x64/ToDecString.asm. cdecl.
;
;  void Fullint64ToDecStringWin(value, valueBuf, resultBuf, int lenValue, ref int lenResult)
;    lenResult: in = the result buffer length, out = the start of the chars
;
;  Same algorithm: copy the value; each pass divides it in place by 10^19 word by word
;  (dropping the top word once it becomes zero), the remainder gives up to 19 chars from the
;  end of the buffer (zero-padded between chunks); stop when the value is zero.
;    div r64 by 10^19 -> DIGIT10E19 twice, inline: Knuth D with 32-bit digits, the divisor
;                        10^19 = 8AC72304_89E80000h is already normalized (top bit set), so no
;                        shifts; the same math as fi_div128 (Div128.asm) without the call
;    x / 10           -> two div r32 (high dword, then low dword). x64 multiplies by the
;                        reciprocal here; on x86 that is four mul r32 and measured no faster
;                        (+-5%), so the divisions stay.
;
;  Register map (x64 -> x86)
;    rsi temp value item -> esi     rcx result buffer -> edi (char phase)
;    r11 10^19 -> D_HI immediate + edi = D_LO (pass)     rdi value length -> LEN
;    r9 words left -> LEFT          r10 result index -> RIDX     rbx chars left -> DCNT
;    rdx remainder / rax chunk -> ebp:ecx (high:low)     r12 -> TEN (div r32 operand)
; =============================================================================

section .text

%define RIDX   esp
%define LEFT   esp+4
%define DCNT   esp+8
%define LEN    esp+12
%define TEN    esp+16
%define FRAME  20

%define D_HI   8AC72304h          ; 10^19 = 8AC72304_89E80000h
%define D_LO   89E80000h

; one 32-bit quotient digit of (top:mid:low) / 10^19
;  in : edx top, eax mid (top:mid < 10^19), ecx low, edi = D_LO
;  out: ebx the digit, ebp:ecx the remainder (< 10^19); eax, edx clobbered
;  The test qhat*D_LO > rhat:low uses the whole divisor, so the loop leaves the exact digit
;  and the remainder rhat:low - qhat*D_LO is exact mod 2^64 (no add-back step).
%macro DIGIT10E19 0
	cmp edx, D_HI
	jb %%div
	mov ebx, 0FFFFFFFFh           ; top == D_HI: the estimate would not fit a digit
	mov ebp, eax
	add ebp, D_HI                 ; rhat = mid + D_HI
	jc %%rem                      ; rhat >= 2^32: the test below cannot fail
	jmp %%refine
%%div:
	mov ebx, D_HI
	div ebx                       ; eax = qhat, edx = rhat
	mov ebx, eax
	mov ebp, edx
%%refine:                         ; while qhat * D_LO > rhat:low: qhat--, rhat += D_HI
	mov eax, ebx
	mul edi
	cmp edx, ebp
	jb %%rem
	ja %%dec
	cmp eax, ecx
	jbe %%rem
%%dec:
	dec ebx
	add ebp, D_HI
	jnc %%refine                  ; rhat still < 2^32: test again
%%rem:                            ; remainder = rhat:low - qhat * D_LO
	mov eax, ebx
	mul edi
	sub ecx, eax
	sbb ebp, edx
%endmacro

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
	mov [LEN], ecx                ; value len
	mov eax, [esp+FRAME+36]
	mov eax, [eax]
	mov [RIDX], eax               ; result len value (ref int)
	mov dword [TEN], 10

.mainLoop:
	mov edi, D_LO
	xor ebp, ebp                  ; remainder = 0
	xor ecx, ecx
	mov eax, [LEN]
	mov [LEFT], eax
.div2longLoop:
	;exif if it's the end of dividend
	cmp dword [LEFT], 0
	je .div2longDone

	;read next word, (remainder:word) / 10^19: two 32-bit digits, the high one first
	mov edx, ebp
	mov eax, ecx
	mov ecx, [esi + 4]
	DIGIT10E19
	mov [esi + 4], ebx            ; quotient high dword
	mov edx, ebp
	mov eax, ecx
	mov ecx, [esi]
	DIGIT10E19
	mov [esi], ebx                ; quotient low dword
	add esi, 8

	or ebx, [esi - 4]
	jnz .storeDiv
	mov eax, [LEN]
	cmp eax, [LEFT]
	jne .storeDiv
	dec dword [LEN]               ; decrement the result len for the next iterations
.storeDiv:
	dec dword [LEFT]
	jmp .div2longLoop

.div2longDone:
	;restore addres to the normal start of the rezult buff
	mov eax, [LEN]
	shl eax, 3
	sub esi, eax

	; the chunk (< 10^19) is ebp:ecx
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

	;convert value 0 - 9 to char and store to the rez buff
	add dl, 30h
	mov ebx, [RIDX]
	mov [edi + ebx], dl

	or eax, ebp
	jnz .charLoop

	;compare the temp value with zero and break if zero
	cmp dword [LEN], 1            ; compare by len (normilazed value)
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
