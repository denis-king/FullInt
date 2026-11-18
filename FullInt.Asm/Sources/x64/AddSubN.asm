default rel
%include "abi.inc"
section .text

; =============================================================================
;  Equal-length add / sub without the general kernels' length resolution (Add.asm / Sub.asm:
;  a Win64 wrapper, three runs, the magnitude compare in Sub): for the many short calls of the
;  engine and the Karatsuba / Toom levels, where that entry cost more than the words (a + b on
;  1-8 words: 4.9-9.1 ns against GMP's mpn_add_n 1.4-2.8). Only volatile registers, no frame.
;
;  Windows x64, limbs big-endian (index 0 = most significant):
;    uint64 Fullint64AddNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;         R = A + B (n words, n >= 1); returns the carry out of the top word
;    uint64 Fullint64SubNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;         R = A - B mod β^n; returns the borrow (1 when A < B)
;  R may be A or B (every word is read before its place is written, from the low end).
;  The carry stays in CF: the loop control is lea / dec / jrcxz (dec keeps CF).
;
;  Registers: r10 / r11 / r8 past the low words of A / B / R, rcx the groups of four, r9 the
;  single words (n mod 4, first), rax / rdx the words in work.
; =============================================================================

%macro ADDSUBN 1
	lea r10, [rcx + r9*8]       ; past A's lowest word
	lea r11, [rdx + r9*8]
	lea r8, [r8 + r9*8]
	mov rcx, r9
	shr rcx, 2                  ; groups of four
	and r9, 3                   ; single words first
	test r9, r9                 ; (CF = 0)
	jz %%groups
%%single:
	mov rax, [r10 - 8]
	%1 rax, [r11 - 8]
	mov [r8 - 8], rax
	lea r10, [r10 - 8]
	lea r11, [r11 - 8]
	lea r8, [r8 - 8]
	dec r9
	jnz %%single
%%groups:
	jrcxz %%done
%%group:
	mov rax, [r10 - 8]
	%1 rax, [r11 - 8]
	mov [r8 - 8], rax
	mov rdx, [r10 - 16]
	%1 rdx, [r11 - 16]
	mov [r8 - 16], rdx
	mov rax, [r10 - 24]
	%1 rax, [r11 - 24]
	mov [r8 - 24], rax
	mov rdx, [r10 - 32]
	%1 rdx, [r11 - 32]
	mov [r8 - 32], rdx
	lea r10, [r10 - 32]
	lea r11, [r11 - 32]
	lea r8, [r8 - 32]
	dec rcx
	jnz %%group
%%done:
	setc al
	movzx eax, al
	WRET
%endmacro
ENTRY Fullint64AddNWIN
	WIN64_ARGS
	ADDSUBN adc
ENTRY Fullint64SubNWIN
	WIN64_ARGS
	ADDSUBN sbb

; =============================================================================
;  uint64 Fullint64SubCmpNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
;
;  |A - B| for the engine's subtraction of equal lengths (n >= 1) in one call: the compare from
;  the top, the order (the larger minus the smaller), the subtraction and the length. Only
;  volatile registers, no frame (the C++ caller then keeps almost nothing live around it).
;
;  The words from the first differing one (index i) down are written to R at their own places,
;  R[i .. n-1]; R[0 .. i-1] is not touched. Returns
;    bits 0-31   off: the zero words above the difference (i + its leading zero words);
;                n when A == B (nothing written)
;    bit 63      1 when B > A (the operands swapped: R = B - A)
;  so the difference is R[off .. n-1], n - off words. R may be A or B (each word is read before
;  its place is written: the compare writes nothing), but no other overlap.
;
;  Registers: rcx / rdx A / B (then the groups of four), r10 / r11 / r8 past the low words of
;  the larger / the smaller / R, r9 n (then the single words), rax the word in work, rdx the
;  result (off | swap << 63) after the setup.
; =============================================================================
ENTRY Fullint64SubCmpNWIN
	WIN64_ARGS
	xor r10d, r10d              ; i
	align 16
.cmp:
	mov rax, [rcx + r10*8]
	cmp rax, [rdx + r10*8]
	jne .diff
	inc r10
	cmp r10, r9
	jne .cmp
	mov rax, r9                 ; A == B: off = n
	WRET
.diff:                          ; CF = A[i] < B[i]
	mov rax, rcx
	cmovb rcx, rdx              ; the larger
	cmovb rdx, rax              ; the smaller
	sbb rax, rax                ; -1 when swapped (mov / cmov keep the flags)
	lea r11, [rdx + r9*8]       ; past the smaller's lowest word
	lea r8, [r8 + r9*8]
	lea rcx, [rcx + r9*8]
	sub r9, r10                 ; the words to subtract: n - i
	shl rax, 63
	lea rdx, [rax + r10]        ; off (so far i) | swap
	mov r10, rcx
	mov rcx, r9
	shr rcx, 2                  ; groups of four
	and r9, 3                   ; single words first
	test r9, r9                 ; (CF = 0)
	jz .groups
.single:
	mov rax, [r10 - 8]
	sbb rax, [r11 - 8]
	mov [r8 - 8], rax
	lea r10, [r10 - 8]
	lea r11, [r11 - 8]
	lea r8, [r8 - 8]
	dec r9
	jnz .single
.groups:
	jrcxz .done
	align 16
.group:
	mov rax, [r10 - 8]
	sbb rax, [r11 - 8]
	mov [r8 - 8], rax
	mov rax, [r10 - 16]
	sbb rax, [r11 - 16]
	mov [r8 - 16], rax
	mov rax, [r10 - 24]
	sbb rax, [r11 - 24]
	mov [r8 - 24], rax
	mov rax, [r10 - 32]
	sbb rax, [r11 - 32]
	mov [r8 - 32], rax
	lea r10, [r10 - 32]
	lea r11, [r11 - 32]
	lea r8, [r8 - 32]
	dec rcx
	jnz .group
.done:                          ; r8 = &R[i], rax = R[i] (the last word written)
	test rax, rax
	jnz .found
.scan:                          ; R[i] is zero only when A[i], B[i] differ by one: the
	add r8, 8                   ; difference is nonzero, a nonzero word is below
	inc rdx
	cmp qword [r8], 0
	je .scan
.found:
	mov rax, rdx
	WRET
