; =============================================================================
;  x86 (32-bit) bitwise AND / OR / XOR with SSE2 (Pentium 4, Core 2 and later), next to
;  Logic.asm (the baseline, kept as is); picked at start-up when Fullint64CpuFeatures
;  (x86/Cpu.asm) reports SSE2. cdecl. Same contract as Fullint64AndWIN etc.:
;
;    int Fullint64AndWIN_sse2(A, B, R, int lenA, int lenB, int lenR, int signs)
;    lenR >= max(lenA, lenB) + 1; returns 1 when the result is negative.
;
;  Both operands non-negative (signs = 0, the common case): no two's complement carries,
;  the operation is word-wise - four words per pass (two 16-byte pand / por / pxor), from
;  the low end, as x64/LogicAvx2.asm:
;    the overlap (the shorter length):  R = A op B
;    the longer operand's other words:  AND: zero;  OR / XOR: copied
;    the words above:                   zero
;  A negative operand: a tail jump into the baseline kernel with the arguments untouched.
;  (The baseline keeps three carry chains on the stack for every dword: ~10x GMP.)
;
;  Registers: esi / edi / ebp walk down A / B / R from their ends; ecx overlap words,
;  ebx the longer operand's other words, edx the zero words above, eax = 1: B is longer.
; =============================================================================

section .text

extern Fullint64AndWIN
extern Fullint64OrWIN
extern Fullint64XorWIN

; %1 words at [src1 - ..] (op [src2 - ..]) -> [dst - ..], walking down; %2 vector op or
; movdqu-only when %3 is empty. Used for the overlap (two sources) and the copy (one).
%macro DOWN2 4 ; %1 count reg, %2 vector op, %3 scalar op, %4 1: two sources
%%quads:
	cmp %1, 4
	jb %%pairs
	sub esi, 32
%if %4
	sub edi, 32
%endif
	sub ebp, 32
	movdqu xmm0, [esi]
	movdqu xmm1, [esi + 16]
%if %4
	movdqu xmm2, [edi]
	movdqu xmm3, [edi + 16]
	%2 xmm0, xmm2
	%2 xmm1, xmm3
%endif
	movdqu [ebp], xmm0
	movdqu [ebp + 16], xmm1
	sub %1, 4
	jmp %%quads
%%pairs:
	cmp %1, 2
	jb %%word
	sub esi, 16
%if %4
	sub edi, 16
%endif
	sub ebp, 16
	movdqu xmm0, [esi]
%if %4
	movdqu xmm2, [edi]
	%2 xmm0, xmm2
%endif
	movdqu [ebp], xmm0
	sub %1, 2
%%word:
	test %1, %1
	jz %%done
	sub esi, 8
%if %4
	sub edi, 8
%endif
	sub ebp, 8
	movq xmm0, [esi]
%if %4
	movq xmm2, [edi]
	%2 xmm0, xmm2
%endif
	movq [ebp], xmm0
%%done:
%endmacro

%macro BITWISE_SSE2 3 ; %1 name, %2 vector op, %3 1: the longer operand's other words are copied (OR, XOR), 0: zero (AND)

global Fullint64%1WIN_sse2
Fullint64%1WIN_sse2:
	cmp dword [esp+28], 0       ; signs
	jne Fullint64%1WIN          ; a negative operand: the baseline kernel
	push ebx
	push esi
	push edi
	push ebp
	; arguments: [esp+20] A, +24 B, +28 R, +32 lenA, +36 lenB, +40 lenR
	mov esi, [esp+20]
	mov edi, [esp+24]
	mov ebp, [esp+28]
	mov eax, [esp+32]
	mov ecx, [esp+36]
	mov edx, [esp+40]
	lea esi, [esi + eax*8]      ; ends (one past the lowest word)
	lea edi, [edi + ecx*8]
	lea ebp, [ebp + edx*8]
	mov ebx, eax
	sub ebx, ecx                ; lenA - lenB
	jb %%bLonger
	sub edx, eax                ; words above = lenR - lenA; overlap = lenB (ecx)
	xor eax, eax
	jmp %%overlap
%%bLonger:
	neg ebx
	sub edx, ecx                ; words above = lenR - lenB
	mov ecx, eax                ; overlap = lenA
	mov eax, 1

%%overlap:
	DOWN2 ecx, %2, 0, 1

%if %3
	test eax, eax
	jz %%copy
	mov esi, edi                ; B is the longer one
%%copy:
	DOWN2 ebx, movdqu, 0, 0
%else
	add edx, ebx                ; AND: the longer operand's other words are zero as well
%endif

	pxor xmm0, xmm0             ; edx >= 1 zero words up to R's top
%%zeroPairs:
	cmp edx, 2
	jb %%zeroWord
	sub ebp, 16
	movdqu [ebp], xmm0
	sub edx, 2
	jmp %%zeroPairs
%%zeroWord:
	test edx, edx
	jz %%done
	sub ebp, 8
	movq [ebp], xmm0
%%done:
	pop ebp
	pop edi
	pop esi
	pop ebx
	xor eax, eax                ; non-negative result
	ret
%endmacro

BITWISE_SSE2 And, pand, 0
BITWISE_SSE2 Or,  por,  1
BITWISE_SSE2 Xor, pxor, 1
