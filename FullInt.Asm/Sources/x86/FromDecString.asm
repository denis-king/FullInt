
; =============================================================================
;  x86 (32-bit) port of x64/FromDecString.asm. cdecl.
;
;  void Fullint64FromDecStringWin(value, valueBuf, resultBuf, int lenValue, ref int lenResult)
;    value: ASCII digits; lenResult: in = the result buffer length (words), out = the start
;
;  Same algorithm: parse 19-digit chunks (the first one shorter) into base-10^19 words, then
;  each pass divides that number by 2^64 from the top: word = (carry·10^19 + word) >> 64,
;  carry = the low 64 bits; the final carry is the next result word from the low end. The top
;  word is dropped once it becomes zero.
;    rax·10 + digit on 64 bits -> 32-bit mul / imul
;    carry·10^19 (64x64)       -> four mul r32
;
;  Register map (x64 -> x86)
;    rdi string item -> esi        rsi temp words -> edi (parse) / esi (passes)
;    r9 chars / words left -> ebp (parse) / LEFT (passes)
;    rax chunk / carry -> ecx:ebx (parse) / CHI:CLO (passes)
;    rbx chunk digits left -> BLK    r10 word count / result index -> CNT / RIDX
;    rdi value length -> ebp (passes)    r13 temp base -> BASE    rcx result -> edi (passes)
;
;  After a pass both versions stop by length with `cmp len, 1` / jb / ja, as ToDecString does
;  (x64 had `test rdi, 1` there until 898c2aa).
; =============================================================================

%include "abi86.inc"
CODE_SECTION

; eax = the value of the 4 decimal digits in eax (as loaded: the first char the low byte, the most
; significant digit); edx scratch
%macro DIGITS4 0
	sub eax, 30303030h
	mov edx, eax
	shr edx, 8
	imul eax, eax, 10
	add eax, edx
	and eax, 00FF00FFh            ; pairs
	mov edx, eax
	shr edx, 16
	imul eax, eax, 100
	add eax, edx
	and eax, 0FFFFh               ; four
%endmacro

%define CNT    esp
%define CLO    esp+4
%define CHI    esp+8
%define LEFT   esp+12
%define BASE   esp+16
%define RIDX   esp+20
%define BLK    esp+24
%define A2     esp+28
%define A3     esp+32
%define FRAME  36

%define TEN19_LO 89E80000h        ; 10^19 = 8AC72304_89E80000h
%define TEN19_HI 8AC72304h

ENTRY Fullint64FromDecStringWin
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRAME
	; arguments: [esp+FRAME+20] value, +24 valueBuf, +28 resultBuf, +32 lenValue, +36 &lenResult
	mov esi, [esp+FRAME+20]       ; the chars
	mov edi, [esp+FRAME+24]       ; the temp words
	mov ebp, [esp+FRAME+32]       ; string len
	mov dword [CNT], 0

	mov eax, ebp                  ; the first chunk takes len % 19 digits (19 when 0)
	xor edx, edx
	mov ecx, 19
	div ecx
	mov dword [BLK], 19
	test edx, edx
	jz .readLoop
	mov [BLK], edx

.readLoop:
	cmp dword [BLK], 19
	je .full
	xor ebx, ebx                  ; chunk = 0 (ecx:ebx)
	xor ecx, ecx
.parseBloch:
	dec ebp
	js .parseBlockDone
	imul ecx, ecx, 10             ; chunk·10 (< 10^19, fits 64 bits)
	mov eax, ebx
	mov edx, 10
	mul edx
	mov ebx, eax
	add ecx, edx
	movzx eax, byte [esi]         ; + digit
	inc esi
	sub eax, 30h
	add ebx, eax
	adc ecx, 0
	dec dword [BLK]
	jnz .parseBloch
	jmp .parseBlockDone
.full:                            ; a full chunk, 19 digits = 8 + 8 + 3: four at once (SWAR, as x64's
	mov eax, [esi]                ; eight), chunk = (A * 10^8 + B) * 1000 + C
	DIGITS4
	mov ebx, eax
	mov eax, [esi + 4]
	DIGITS4
	imul ebx, ebx, 10000
	add ebx, eax                  ; A: the first eight
	mov eax, [esi + 8]
	DIGITS4
	mov ecx, eax
	mov eax, [esi + 12]
	DIGITS4
	imul ecx, ecx, 10000
	add ecx, eax                  ; B: the next eight
	mov eax, ebx
	mov edx, 100000000
	mul edx
	add eax, ecx
	adc edx, 0                    ; A * 10^8 + B (< 10^16)
	imul ecx, edx, 1000
	mov edx, 1000
	mul edx
	add edx, ecx                  ; * 1000
	mov ebx, eax
	mov ecx, edx
	movzx eax, byte [esi + 16]    ; C: the last three
	sub eax, 30h
	movzx edx, byte [esi + 17]
	sub edx, 30h
	lea eax, [eax + eax * 4]
	lea eax, [edx + eax * 2]
	movzx edx, byte [esi + 18]
	sub edx, 30h
	lea eax, [eax + eax * 4]
	lea eax, [edx + eax * 2]
	add ebx, eax
	adc ecx, 0
	add esi, 19
	sub ebp, 19
.parseBlockDone:
	mov eax, [CNT]
	mov [edi + eax*8], ebx
	mov [edi + eax*8 + 4], ecx
	inc dword [CNT]

	mov dword [BLK], 19
	cmp ebp, 0
	jg .readLoop

.readDone:
	mov [BASE], edi               ; store the temp base
	mov ebp, [CNT]                ; value len
	mov eax, [esp+FRAME+36]
	mov eax, [eax]
	mov [RIDX], eax               ; result len value (ref int)
	mov edi, [esp+FRAME+28]       ; result buffer

.mainLoop:
	mov [LEFT], ebp
	mov esi, [BASE]               ; restore the temp address
	xor eax, eax
	mov [CLO], eax                ; carry = 0
	mov [CHI], eax
.div2longLoop:
	;exif if it's the end of dividend
	cmp dword [LEFT], 0
	je .div2longDone

	; acc = carry·10^19 + word (128 bits: A3 A2 ecx ebx)
	mov eax, [CLO]
	mov edx, TEN19_LO
	mul edx
	mov ebx, eax                  ; A0
	mov ecx, edx                  ; A1
	mov eax, [CLO]
	mov edx, TEN19_HI
	mul edx
	add ecx, eax
	adc edx, 0
	mov [A2], edx
	mov dword [A3], 0
	mov eax, [CHI]
	mov edx, TEN19_LO
	mul edx
	add ecx, eax
	adc [A2], edx
	adc dword [A3], 0
	mov eax, [CHI]
	mov edx, TEN19_HI
	mul edx
	add [A2], eax
	adc [A3], edx
	add ebx, [esi]                ; + word
	adc ecx, [esi + 4]
	adc dword [A2], 0
	adc dword [A3], 0

	mov eax, [A2]                 ; acc >> 64 -> the word
	mov edx, [A3]
	mov [esi], eax
	mov [esi + 4], edx
	mov [CLO], ebx                ; acc low 64 bits -> carry
	mov [CHI], ecx
	add esi, 8

	;decrease the buffer size
	or eax, edx
	jnz .div2longDec
	cmp ebp, [LEFT]
	jne .div2longDec
	dec ebp
	add dword [BASE], 8
.div2longDec:
	dec dword [LEFT]
	jmp .div2longLoop

.div2longDone:
	dec dword [RIDX]
	js .bufferOut
	mov eax, [RIDX]
	mov ecx, [CLO]
	mov [edi + eax*8], ecx
	mov ecx, [CHI]
	mov [edi + eax*8 + 4], ecx

	;exit if the buffer is empty
	cmp ebp, 1
	jb .done
	ja .mainLoop
	mov esi, [BASE]
	mov eax, [esi]
	or eax, [esi + 4]
	jz .done
	jmp .mainLoop

.bufferOut:
	mov dword [RIDX], 0
.done:
	mov eax, [esp+FRAME+36]
	mov ecx, [RIDX]
	mov [eax], ecx                ;return the start position of the result words (ref int)
	add esp, FRAME
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
