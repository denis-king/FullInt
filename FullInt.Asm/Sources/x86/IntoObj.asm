; =============================================================================
;  x86 (32-bit): the C++ into-operations' common short cases on the number objects themselves
;  (C++ only: FullInt's layout, checked by static_asserts in FullIntEngine.cpp). cdecl.
;
;  MSVC on x86 makes no tail calls and keeps much of the C++ paths' state on the stack: a thin
;  C++ entry in front of the general function cost more than it saved. These kernels do the
;  whole common case - the checks, the words' addresses, the loop, the result's header - and
;  return 0; anything else returns 1 with nothing written, and the caller takes the general path.
;
;  FullInt (x86):  +0   inline words (8) / the heap pointer (when cap > 8)
;                    +64  cap   +68 off   +72 size   +76 neg (byte)
;  A number's words: base + off*8, size of them, index 0 = the most significant word.
;
;    int Fullint64AndObjWIN(FullInt* r, const FullInt* a, const FullInt* b)
;      r = a & b when both are non-negative, r is neither and has room for the shorter's
;      length: eight words a pass with SSE2 (movdqu / pand; the C++ x86 build needs SSE2 anyway),
;      then four, two, one.
;
;    int Fullint64AddObjWIN(FullInt* r, const FullInt* a, const FullInt* b, int negate2)
;      r = a + b (negate2 = 0) or a - b (1) when r is neither and has room (the longer's length,
;      + 1 for a sum); any lengths. Same signs: the words added from the low end with the carry in
;      CF, then the carry through the longer's other words, the carry word on top. Opposite signs:
;      the larger magnitude (the longer; equal lengths: compared from the top, the equal top words
;      left out) minus the smaller with the borrow in CF, the leading zero words counted.
;
;    int Fullint64ShlObjWIN(FullInt* r, const FullInt* a, int shift)
;      r = a << shift when r is not a, a is not zero, shift > 0 and not a multiple of 64, and r
;      has room: la + shift/64 + 1 words. Words of r from the top: r[i] = a[i-1] << b | a[i] >>
;      (64 - b), b = shift % 64, with SSE2's 64-bit lane shifts (psllq / psrlq), two words a pass
;      (shld r, r, cl: several uops - 1.3-1.4x slower from 32 words); then shift/64 zero words.
;      Only r[0] can be zero.
; =============================================================================

%include "abi86.inc"
CODE_SECTION

%define OBJ_CAP  64
%define OBJ_OFF  68
%define OBJ_SIZE 72
%define OBJ_NEG  76
%define OBJ_INLINE 8

; %1 = the object, %2 = its base (out): the heap pointer or the object itself
%macro OBJ_BASE 2
	mov %2, %1
	cmp dword [%1 + OBJ_CAP], OBJ_INLINE
	jbe %%inline
	mov %2, [%1]
%%inline:
%endmacro

ENTRY Fullint64AndObjWIN
	push ebp
	push ebx
	push esi
	push edi
	mov ebp, [esp + 20]           ; r
	mov esi, [esp + 24]           ; a
	mov edi, [esp + 28]           ; b
	cmp ebp, esi
	je .no
	cmp ebp, edi
	je .no
	mov al, [esi + OBJ_NEG]
	or al, [edi + OBJ_NEG]
	jnz .no
	mov ecx, [esi + OBJ_SIZE]     ; la
	mov edx, [edi + OBJ_SIZE]     ; lb
	cmp ecx, edx
	jae .ordered
	xchg esi, edi                 ; a the longer
	xchg ecx, edx
.ordered:                         ; the result: lb words (a's low lb words & b's)
	cmp edx, [ebp + OBJ_CAP]
	ja .no
	sub ecx, edx                  ; la - lb
	add ecx, [esi + OBJ_OFF]
	OBJ_BASE esi, eax
	lea esi, [eax + ecx*8]        ; a's low lb words
	mov ecx, [edi + OBJ_OFF]
	OBJ_BASE edi, eax
	lea edi, [eax + ecx*8]        ; b's words
	OBJ_BASE ebp, ebx             ; r's words from its base
	mov [ebp + OBJ_SIZE], edx     ; n (the leading zeros taken off below)
	xor ecx, ecx                  ; the word index
	lea eax, [edx - 8]
	cmp ecx, eax
	jg .quad
	align 16
.oct:                             ; eight words: four 16-byte pand
	movdqu xmm0, [esi + ecx*8]
	movdqu xmm1, [edi + ecx*8]
	movdqu xmm2, [esi + ecx*8 + 16]
	movdqu xmm3, [edi + ecx*8 + 16]
	movdqu xmm4, [esi + ecx*8 + 32]
	movdqu xmm5, [edi + ecx*8 + 32]
	movdqu xmm6, [esi + ecx*8 + 48]
	movdqu xmm7, [edi + ecx*8 + 48]
	pand xmm0, xmm1
	pand xmm2, xmm3
	pand xmm4, xmm5
	pand xmm6, xmm7
	movdqu [ebx + ecx*8], xmm0
	movdqu [ebx + ecx*8 + 16], xmm2
	movdqu [ebx + ecx*8 + 32], xmm4
	movdqu [ebx + ecx*8 + 48], xmm6
	add ecx, 8
	cmp ecx, eax
	jle .oct
.quad:
	lea eax, [edx - 4]
	cmp ecx, eax
	jg .pair
	; four words: two 16-byte pand
	movdqu xmm0, [esi + ecx*8]
	movdqu xmm1, [edi + ecx*8]
	movdqu xmm2, [esi + ecx*8 + 16]
	movdqu xmm3, [edi + ecx*8 + 16]
	pand xmm0, xmm1
	pand xmm2, xmm3
	movdqu [ebx + ecx*8], xmm0
	movdqu [ebx + ecx*8 + 16], xmm2
	add ecx, 4
.pair:
	lea eax, [edx - 2]
	cmp ecx, eax
	jg .single
	movq xmm0, [esi + ecx*8]      ; two words as two movq: no 16-byte access needed
	movq xmm1, [edi + ecx*8]
	movq xmm2, [esi + ecx*8 + 8]
	movq xmm3, [edi + ecx*8 + 8]
	pand xmm0, xmm1
	pand xmm2, xmm3
	movq [ebx + ecx*8], xmm0
	movq [ebx + ecx*8 + 8], xmm2
	add ecx, 2
.single:
	cmp ecx, edx
	je .lead
	movq xmm0, [esi + ecx*8]
	movq xmm1, [edi + ecx*8]
	pand xmm0, xmm1
	movq [ebx + ecx*8], xmm0
.lead:
	xor ecx, ecx                  ; the leading zero words (one word stays)
	dec edx
	jz .header
.zeros:
	mov eax, [ebx + ecx*8]
	or eax, [ebx + ecx*8 + 4]
	jnz .header
	inc ecx
	cmp ecx, edx
	jne .zeros
.header:
	mov [ebp + OBJ_OFF], ecx
	sub [ebp + OBJ_SIZE], ecx
	mov byte [ebp + OBJ_NEG], 0
	xor eax, eax
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
.no:
	mov eax, 1
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; %1 = the object, %2 = its word count (a register), %3 = the base register to use (out: the
; address just past its lowest word)
%macro OBJ_END 3
	OBJ_BASE %1, %3
	add %2, [%1 + OBJ_OFF]
	lea %1, [%3 + %2*8]
%endmacro

; the words' loop from the low end: %1 adc / sbb; esi / edi / ebx past the lowest words of the
; longer / the shorter / r, edx the shorter's words (>= 1), ecx the longer's other words; CF in / out
%macro ADDSUB_WORDS 1
%%word:
	mov eax, [esi - 8]
	%1 eax, [edi - 8]
	mov [ebx - 8], eax
	mov eax, [esi - 4]
	%1 eax, [edi - 4]
	mov [ebx - 4], eax
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ebx, [ebx - 8]
	dec edx
	jnz %%word
	jecxz %%done
%%carry:                          ; the longer's other words with the carry / borrow
	mov eax, [esi - 8]
	%1 eax, 0
	mov [ebx - 8], eax
	mov eax, [esi - 4]
	%1 eax, 0
	mov [ebx - 4], eax
	lea esi, [esi - 8]
	lea ebx, [ebx - 8]
	dec ecx
	jnz %%carry
%%done:
%endmacro

ENTRY Fullint64AddObjWIN
	push ebp
	push ebx
	push esi
	push edi
	mov ebp, [esp + 20]           ; r
	mov esi, [esp + 24]           ; a
	mov edi, [esp + 28]           ; b
	cmp ebp, esi
	je .no
	cmp ebp, edi
	je .no
	movzx eax, byte [esi + OBJ_NEG]
	movzx edx, byte [edi + OBJ_NEG]
	xor edx, [esp + 32]           ; b's sign in the operation
	mov [esp + 24], eax           ; the argument slots as scratch: a's sign
	mov [esp + 28], edx           ; b's
	mov ecx, [esi + OBJ_SIZE]     ; la
	mov edx, [edi + OBJ_SIZE]     ; lb
	cmp eax, [esp + 28]
	jne .sub

	; a + b, same signs: the sum has the longer's length + 1 (the carry word on top)
	cmp ecx, edx
	jae .addOrdered
	xchg esi, edi
	xchg ecx, edx
.addOrdered:
	lea eax, [ecx + 1]
	cmp eax, [ebp + OBJ_CAP]
	ja .no
	mov [ebp + OBJ_SIZE], eax     ; lx + 1 (the offset taken off below)
	mov eax, [esp + 24]
	mov [ebp + OBJ_NEG], al       ; the common sign
	push ecx                      ; lx
	OBJ_END esi, ecx, ebx
	pop ecx
	mov eax, edx
	OBJ_END edi, eax, ebx
	OBJ_BASE ebp, ebx
	lea ebx, [ebx + ecx*8 + 8]    ; past r's lowest word (lx + 1 words)
	sub ecx, edx                  ; the longer's other words
	clc
	ADDSUB_WORDS adc
	setc al                       ; the carry word
	movzx eax, al
	mov [ebx - 8], eax
	mov dword [ebx - 4], 0
	xor eax, 1                    ; the offset: 1 without a carry
	mov [ebp + OBJ_OFF], eax
	sub [ebp + OBJ_SIZE], eax
	xor eax, eax
	jmp .ret

.sub:                             ; opposite signs: the larger magnitude minus the smaller
	cmp ecx, edx
	ja .subLonger
	jb .subSwap
	; equal lengths: compared from the top
	cmp ecx, [ebp + OBJ_CAP]
	ja .no
	mov [ebp + OBJ_SIZE], ecx     ; n
	mov eax, [esi + OBJ_OFF]
	OBJ_BASE esi, ebx
	lea esi, [ebx + eax*8]        ; a's words from the top
	mov eax, [edi + OBJ_OFF]
	OBJ_BASE edi, ebx
	lea edi, [ebx + eax*8]
	xor edx, edx                  ; i
.cmp:
	mov eax, [esi + edx*8 + 4]
	cmp eax, [edi + edx*8 + 4]
	jne .differ
	mov eax, [esi + edx*8]
	cmp eax, [edi + edx*8]
	jne .differ
	inc edx
	cmp edx, ecx
	jne .cmp
	OBJ_BASE ebp, ebx             ; a == b: zero
	xor eax, eax
	mov [ebx], eax
	mov [ebx + 4], eax
	mov [ebp + OBJ_OFF], eax
	mov dword [ebp + OBJ_SIZE], 1
	mov [ebp + OBJ_NEG], al
	jmp .ret
.differ:                          ; CF = a[i] < b[i]
	jae .subFrom
	xchg esi, edi
	mov eax, [esp + 28]
	mov [esp + 24], eax           ; b's sign
.subFrom:                         ; the words below i: n - i of each, no other words
	mov [esp + 28], edx           ; the zero scan starts at i
	lea esi, [esi + ecx*8]        ; past the lowest words
	lea edi, [edi + ecx*8]
	OBJ_BASE ebp, ebx
	lea ebx, [ebx + ecx*8]
	sub ecx, edx
	mov edx, ecx                  ; n - i words
	xor ecx, ecx
	jmp .subWords

.subSwap:
	xchg esi, edi
	xchg ecx, edx
	mov eax, [esp + 28]
	mov [esp + 24], eax           ; b's sign
.subLonger:                       ; the longer is the larger: lx words
	cmp ecx, [ebp + OBJ_CAP]
	ja .no
	mov [ebp + OBJ_SIZE], ecx     ; n
	mov dword [esp + 28], 0       ; the zero scan from the top
	push ecx
	OBJ_END esi, ecx, ebx
	pop ecx
	mov eax, edx
	OBJ_END edi, eax, ebx
	OBJ_BASE ebp, ebx
	lea ebx, [ebx + ecx*8]
	sub ecx, edx

.subWords:
	clc
	ADDSUB_WORDS sbb
	; ebx = r's word at the scan's start; the larger minus the smaller: a nonzero word below
	mov ecx, [esp + 28]
.zeros:
	mov eax, [ebx]
	or eax, [ebx + 4]
	jnz .found
	add ebx, 8
	inc ecx
	jmp .zeros
.found:
	mov [ebp + OBJ_OFF], ecx
	sub [ebp + OBJ_SIZE], ecx
	mov eax, [esp + 24]
	mov [ebp + OBJ_NEG], al       ; the larger's sign
	xor eax, eax
	jmp .ret
.no:
	mov eax, 1
.ret:
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

ENTRY Fullint64ShlObjWIN
	push ebp
	push ebx
	push esi
	push edi
	mov edi, [esp + 20]           ; r
	mov esi, [esp + 24]           ; a
	mov ecx, [esp + 28]           ; shift
	cmp edi, esi
	je .shlNo
	test ecx, ecx
	jle .shlNo                    ; 0 or negative
	test ecx, 63
	jz .shlNo                     ; whole words: a copy (the C++ path)
	mov ebp, [esi + OBJ_SIZE]     ; la
	mov eax, ecx
	shr eax, 6
	lea eax, [eax + ebp + 1]      ; n
	cmp eax, [edi + OBJ_CAP]
	ja .shlNo
	movzx edx, byte [esi + OBJ_NEG]
	mov [esp + 24], edx           ; the sign (a's argument slot as scratch)
	mov edx, [esi + OBJ_OFF]
	OBJ_BASE esi, ebx
	lea esi, [ebx + edx*8]        ; a's words from the top
	cmp ebp, 1
	jne .shlGo
	mov edx, [esi]
	or edx, [esi + 4]
	jz .shlNo                     ; zero
.shlGo:
	mov [edi + OBJ_SIZE], eax     ; n (a leading zero word taken off below)
	mov edx, [esp + 24]
	mov [edi + OBJ_NEG], dl
	push edi                      ; r (the arguments 4 bytes further now)
	OBJ_BASE edi, ebx
	mov edi, ebx                  ; r's words from the top
	and ecx, 63                   ; b
	movd xmm6, ecx
	neg ecx
	add ecx, 64
	movd xmm7, ecx                ; 64 - b
	movq xmm0, [esi]              ; r[0] = a[0] >> (64 - b)
	psrlq xmm0, xmm7
	movq [edi], xmm0
	add edi, 8
	lea ecx, [ebp - 1]            ; r[1 .. la-1]: a[i-1] << b | a[i] >> (64 - b), esi at a[i-1]
	cmp ecx, 2
	jb .shlSingle
.shlPair:                         ; two words: 16-byte psllq / psrlq / por
	movdqu xmm0, [esi]
	movdqu xmm1, [esi + 8]
	psllq xmm0, xmm6
	psrlq xmm1, xmm7
	por xmm0, xmm1
	movdqu [edi], xmm0
	add esi, 16
	add edi, 16
	sub ecx, 2
	cmp ecx, 2
	jae .shlPair
.shlSingle:
	test ecx, ecx
	jz .shlLast
	movq xmm0, [esi]
	movq xmm1, [esi + 8]
	psllq xmm0, xmm6
	psrlq xmm1, xmm7
	por xmm0, xmm1
	movq [edi], xmm0
	add esi, 8
	add edi, 8
.shlLast:                         ; r[la] = a[la-1] << b
	movq xmm0, [esi]
	psllq xmm0, xmm6
	movq [edi], xmm0
.shlZeros:                        ; shift / 64 zero words below r[la]
	mov ecx, [esp + 32]
	shr ecx, 6
	jecxz .shlLead
.zeroWord:
	mov dword [edi + 8], 0
	mov dword [edi + 12], 0
	add edi, 8
	dec ecx
	jnz .zeroWord
.shlLead:                         ; r[0] zero: one word off (a's top bits all went to r[1])
	pop ebp                       ; r
	OBJ_BASE ebp, ebx
	mov eax, [ebx]
	or eax, [ebx + 4]
	cmp eax, 1
	sbb eax, eax
	neg eax                       ; 1 when r[0] is zero
	mov [ebp + OBJ_OFF], eax
	sub [ebp + OBJ_SIZE], eax
	xor eax, eax
	jmp .shlRet
.shlNo:
	mov eax, 1
.shlRet:
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
