; =============================================================================
;  x86 (32-bit): the C++ into-operations' common short cases on the number objects themselves
;  (C++ only: FullInt64's layout, checked by static_asserts in FullInt64Engine.cpp). cdecl.
;
;  MSVC on x86 makes no tail calls and keeps much of the C++ paths' state on the stack: a thin
;  C++ entry in front of the general function cost more than it saved. These kernels do the
;  whole common case - the checks, the words' addresses, the loop, the result's header - and
;  return 0; anything else returns 1 with nothing written, and the caller takes the general path.
;
;  FullInt64 (x86):  +0   inline words (8) / the heap pointer (when cap > 8)
;                    +64  cap   +68 off   +72 size   +76 neg (byte)
;  A number's words: base + off*8, size of them, index 0 = the most significant word.
;
;    int Fullint64AndObjWIN(FullInt64* r, const FullInt64* a, const FullInt64* b)
;      r = a & b when both are non-negative, r is neither and has room for the shorter's
;      length: eight words a pass with SSE2 (movdqu / pand; the C++ x86 build needs SSE2 anyway),
;      then four, two, one.
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

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
