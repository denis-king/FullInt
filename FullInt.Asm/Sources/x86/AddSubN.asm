; =============================================================================
;  x86 (32-bit): equal-length add / sub (x64/AddSubN.asm), the same functions and contracts
;  (cdecl; n an int64 in two stack slots, its low half read; the result in edx:eax):
;    uint64 Fullint64AddNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;    uint64 Fullint64SubNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;  Words big-endian, each word little-endian (low dword at +0); one word (two dwords) a pass,
;  the carry in CF (lea / dec keep it).
; =============================================================================

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

global Fullint64AddNWIN
Fullint64AddNWIN:
	ADDSUBN adc

global Fullint64SubNWIN
Fullint64SubNWIN:
	ADDSUBN sbb
