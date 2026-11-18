; =============================================================================
;  x86 (32-bit): equal-length add / sub (x64/AddSubN.asm), the same functions and contracts
;  (cdecl; n an int64 in two stack slots, its low half read; the result in edx:eax):
;    uint64 Fullint64AddNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;    uint64 Fullint64SubNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;  Words big-endian, each word little-endian (low dword at +0); one word (two dwords) a pass,
;  the carry in CF (lea / dec keep it).
; =============================================================================

%include "abi86.inc"
section .text

%macro ADDSUBN 1
	push esi
	push edi
	push ebx
	mov esi, [esp + 16]         ; A
	mov edi, [esp + 20]         ; B
	mov ebx, [esp + 24]         ; R
	mov ecx, [esp + 28]         ; n
	lea esi, [esi + ecx*8]      ; past the lowest words
	lea edi, [edi + ecx*8]
	lea ebx, [ebx + ecx*8]
	clc
%%word:
	mov eax, [esi - 8]
	%1 eax, [edi - 8]
	mov [ebx - 8], eax
	mov edx, [esi - 4]
	%1 edx, [edi - 4]
	mov [ebx - 4], edx
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ebx, [ebx - 8]
	dec ecx
	jnz %%word
	setc al
	movzx eax, al
	xor edx, edx
	pop ebx
	pop edi
	pop esi
	ret
%endmacro

ENTRY Fullint64AddNWIN
	ADDSUBN adc

ENTRY Fullint64SubNWIN
	ADDSUBN sbb

; =============================================================================
;  uint64 Fullint64SubCmpNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;  (x64/AddSubN.asm): the compare from the top, the larger minus the smaller on the words from
;  the first differing one (written to R at their own places), the zero words above counted.
;  Returns edx:eax = off | swap << 63 (off = n when A == B).
;  R may be A or B (the same places), no other overlap.
;
;  Registers: esi / edi the larger / the smaller, ebx R, ecx the words, ebp off | swap << 31,
;  edx:eax the word in work.
; =============================================================================
ENTRY Fullint64SubCmpNWIN
	push esi
	push edi
	push ebx
	push ebp
	mov esi, [esp + 20]         ; A
	mov edi, [esp + 24]         ; B
	mov ebx, [esp + 28]         ; R
	mov ecx, [esp + 32]         ; n
	xor ebp, ebp                ; i
.cmp:
	mov eax, [esi + ebp*8 + 4]  ; the high dword first
	cmp eax, [edi + ebp*8 + 4]
	jne .diff
	mov eax, [esi + ebp*8]
	cmp eax, [edi + ebp*8]
	jne .diff
	inc ebp
	cmp ebp, ecx
	jne .cmp
	mov eax, ecx                ; A == B: off = n
	xor edx, edx
	jmp .ret
.diff:                          ; CF = A[i] < B[i]
	mov eax, esi
	cmovb esi, edi              ; the larger
	cmovb edi, eax              ; the smaller
	sbb eax, eax                ; -1 when swapped
	lea esi, [esi + ecx*8]      ; past the lowest words
	lea edi, [edi + ecx*8]
	lea ebx, [ebx + ecx*8]
	sub ecx, ebp                ; the words to subtract: n - i
	and eax, 0x80000000
	or ebp, eax                 ; off (so far i) | swap; CF = 0
.word:
	mov eax, [esi - 8]
	sbb eax, [edi - 8]
	mov [ebx - 8], eax
	mov edx, [esi - 4]
	sbb edx, [edi - 4]
	mov [ebx - 4], edx
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ebx, [ebx - 8]
	dec ecx
	jnz .word
	or eax, edx                 ; ebx = &R[i], edx:eax = R[i]
	jnz .found
.scan:                          ; R[i] zero only when A[i], B[i] differ by one
	add ebx, 8
	inc ebp
	mov eax, [ebx]
	or eax, [ebx + 4]
	jz .scan
.found:
	mov eax, ebp
	and eax, 0x7FFFFFFF
	mov edx, ebp
	and edx, 0x80000000
.ret:
	pop ebp
	pop ebx
	pop edi
	pop esi
	ret
