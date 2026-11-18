default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  Exact division by a small constant for the Toom-Cook interpolation (Toom3 / Toom4 /
;  Toom6 in C# and C++), without the general division. Baseline x86-64 instructions only.
;  Words big-endian (word 0 the most significant).
;
;  uint64 Fullint64DivExactWIN(a, q, int len, uint64 d)
;    q = a / d for an a known to be a multiple of d; d = d'·2^t with d' odd, below 2^32;
;    q may be a. Hensel division from the low end on x = a >> t (shrd with the next word up,
;    off the dependency chain): inv = d'^-1 mod 2^64, per word
;      q_i = (x_i - c)·inv,  c' = hi(q_i·d') + (x_i < c)
;    (x_i - c - borrow·2^64 = q_i·d' - hi·2^64). Returns the last c: 0 when a was a multiple.
;    About 1.55x the speed of the reciprocal division by one word (Zen 3: 2.1 vs 3.25 ns per
;    word); a fused shift-and-add for the same callers lost to the separate kernels (the
;    AVX2 shift + the add) and was dropped.
; =============================================================================
ENTRY Fullint64DivExactWIN
	WIN64_ARGS
	push rbx
	push rsi
	push rdi
	mov r10, rcx                ; a
	mov r11, rdx                ; q
	movsxd r8, r8d              ; len
	bsf rcx, r9                 ; t (d != 0)
	shr r9, cl                  ; d'
	; inv = d'^-1 mod 2^64: (3d') xor 2 is right in 5 bits, each x·(2 - d'·x) doubles them
	lea rbx, [r9 + r9*2]
	xor rbx, 2
%rep 4
	mov rax, r9
	imul rax, rbx               ; d'·x
	neg rax
	add rax, 2                  ; 2 - d'·x
	imul rbx, rax
%endrep
	xor esi, esi                ; c
	dec r8                      ; the low word
	js .done
	jz .top
.loop:
	mov rax, [r10 + r8*8]
	mov rdi, [r10 + r8*8 - 8]   ; the next word up
	shrd rax, rdi, cl           ; x_i
	sub rax, rsi                ; x_i - c
	sbb rsi, rsi                ; -borrow
	imul rax, rbx               ; q_i
	mov [r11 + r8*8], rax
	mul r9                      ; rdx = hi(q_i·d')
	sub rdx, rsi                ; + borrow
	mov rsi, rdx
	dec r8
	jnz .loop
.top:
	mov rax, [r10]
	shr rax, cl                 ; the top word: nothing above it
	sub rax, rsi
	sbb rsi, rsi
	imul rax, rbx
	mov [r11], rax
	mul r9
	sub rdx, rsi
	mov rsi, rdx
.done:
	mov rax, rsi
	pop rdi
	pop rsi
	pop rbx
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
