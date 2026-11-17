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
