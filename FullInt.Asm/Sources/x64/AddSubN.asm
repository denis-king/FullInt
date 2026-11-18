default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  Equal-length add / sub without the general kernels' length resolution (Add.asm / Sub.asm:
;  a Win64 wrapper, three runs, the magnitude compare in Sub): for the many short calls of the
;  engine and the Karatsuba / Toom levels, where that entry cost more than the words (a + b on
;  1-8 words: 4.9-9.1 ns against GMP's mpn_add_n 1.4-2.8). Under four words only volatile
;  registers; from four, rsi too (one push: the fourth word register of the group loop).
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
;  single words (n mod 4, first, rax the word). The group loop software-pipelined: four word
;  registers (r9 / rsi / rax / rdx), the next group's A words loaded at the end of a pass, the
;  loop entered in its middle (12-1024 words 3-8% faster than one register pair in turn).
; =============================================================================

%macro ADDSUBN 2                ; %1 adc / sbb, %2 the exit (CF the carry, r8 the top word written)
	lea r10, [rcx + r9*8]       ; past A's lowest word
	lea r11, [rdx + r9*8]
	lea r8, [r8 + r9*8]
	mov rcx, r9
	shr rcx, 2                  ; groups of four
	and r9, 3                   ; single words first
	jz %%groups                 ; (and: CF = 0)
	align 16
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
	jrcxz %%none
	SAVE rsi
	mov r9, [r10 - 8]           ; the first group's A words loaded ahead
	mov rsi, [r10 - 16]
	dec rcx                     ; (keeps CF)
	jmp %%mid
%%none:                         ; (here: jrcxz reaches it)
	%2
	align 32
%%group:
	%1 r9, [r11 - 8]
	%1 rsi, [r11 - 16]
	%1 rax, [r11 - 24]
	%1 rdx, [r11 - 32]
	mov [r8 - 8], r9
	lea r10, [r10 - 32]
	mov [r8 - 16], rsi
	mov [r8 - 24], rax
	dec rcx
	mov [r8 - 32], rdx
	lea r11, [r11 - 32]
	mov r9, [r10 - 8]           ; the next group's A words
	mov rsi, [r10 - 16]
	lea r8, [r8 - 32]
%%mid:
	mov rax, [r10 - 24]
	mov rdx, [r10 - 32]
	jnz %%group
	%1 r9, [r11 - 8]            ; the last group
	%1 rsi, [r11 - 16]
	%1 rax, [r11 - 24]
	%1 rdx, [r11 - 32]
	mov [r8 - 8], r9
	mov [r8 - 16], rsi
	mov [r8 - 24], rax
	mov [r8 - 32], rdx
	lea r8, [r8 - 32]
	RESTORE rsi
	%2
%endmacro

%macro EXIT_CARRY 0             ; returns the carry
	setc al
	movzx eax, al
	WRET
%endmacro

%macro EXIT_HDR_ADD 0           ; the header (in the home area) and the carry word above the sum
	setc al
	movzx eax, al
	mov rcx, [rsp + 8]
	mov [r8 - 8], rax           ; R[0] = the carry
	add [rcx + 4], eax          ; size = n + carry
	xor eax, 1
	mov [rcx], eax              ; off = 1 - carry
	WRET
%endmacro

	align 32
ENTRY Fullint64AddNWIN
	WIN64_ARGS
	ADDSUBN adc, EXIT_CARRY
	align 32
ENTRY Fullint64SubNWIN
	WIN64_ARGS
fi_subn_body:                   ; (SubCmpNHdr's fast path continues here)
	ADDSUBN sbb, EXIT_CARRY

; =============================================================================
;  The engine's into-paths with the number's header written here too, so the C++ caller keeps
;  nothing across the call and jumps to the kernel (it saved three registers and set a frame):
;    struct Header { uint32 off; uint32 size; uint8 neg; }   (FullInt64's off_, size_, neg_)
;
;  void Fullint64AddNHdrWIN(const uint64* A, const uint64* B, uint64* R, Header* h)
;       in: h->size = n (>= 1). R[1 .. n] = A + B, R[0] = the carry; h->off = 1 - carry,
;       h->size = n + carry (h->neg untouched). R must not overlap A or B.
;  void Fullint64SubCmpNHdrWIN(const uint64* A, const uint64* B, uint64* R, Header* h)
;       in: h->size = n (>= 1), h->neg = A's sign. |A - B| as Fullint64SubCmpNWIN; h->off /
;       h->size the difference's place, h->neg flipped when B > A; A == B: R[0] = 0, off 0,
;       size 1, neg false.
;  The header pointer waits in the home area (rcx / r9 are word registers in the loops).
; =============================================================================
	align 32
ENTRY Fullint64AddNHdrWIN
	WIN64_ARGS
	mov [rsp + 8], r9           ; the header
	mov r9d, [r9 + 4]           ; n
	add r8, 8                   ; the sum from R[1]
	ADDSUBN adc, EXIT_HDR_ADD

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
%macro SUBCMPN 2               ; %1 the exit for A == B, %2 the exit with rdx = off | swap << 63
	xor r10d, r10d              ; i
	align 16
%%cmp:
	mov rax, [rcx + r10*8]
	cmp rax, [rdx + r10*8]
	jne %%diff
	inc r10
	cmp r10, r9
	jne %%cmp
	%1                          ; A == B
%%diff:                          ; CF = A[i] < B[i]
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
	jz %%groups
%%single:
	mov rax, [r10 - 8]
	sbb rax, [r11 - 8]
	mov [r8 - 8], rax
	lea r10, [r10 - 8]
	lea r11, [r11 - 8]
	lea r8, [r8 - 8]
	dec r9
	jnz %%single
%%groups:
	jrcxz %%done
	align 32
%%group:
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
	jnz %%group
%%done:                          ; r8 = &R[i], rax = R[i] (the last word written)
	test rax, rax
	jnz %%found
%%scan:                          ; R[i] is zero only when A[i], B[i] differ by one: the
	add r8, 8                   ; difference is nonzero, a nonzero word is below
	inc rdx
	cmp qword [r8], 0
	je %%scan
%%found:
	%2                          ; rdx = off | swap << 63
%endmacro

%macro EXIT_EQ_OFF 0
	mov rax, r9                 ; off = n
	WRET
%endmacro
%macro EXIT_OFF 0
	mov rax, rdx
	WRET
%endmacro
%macro EXIT_EQ_HDR 0            ; zero: R[0] = 0, off 0, size 1, neg false
	mov rcx, [rsp + 8]
	mov qword [r8], 0
	mov dword [rcx], 0
	mov dword [rcx + 4], 1
	mov byte [rcx + 8], 0
	WRET
%endmacro
%macro EXIT_HDR_SUB 0
	mov rcx, [rsp + 8]
	mov [rcx], edx              ; off
	mov eax, [rcx + 4]
	sub eax, edx
	mov [rcx + 4], eax          ; size = n - off
	shr rdx, 63
	xor [rcx + 8], dl           ; neg ^= swap
	WRET
%endmacro

	align 32
ENTRY Fullint64SubCmpNWIN
	WIN64_ARGS
	SUBCMPN EXIT_EQ_OFF, EXIT_OFF
; SubCmpNHdr: the length is known before the subtraction unless the first differing words differ
; by one (only then can the difference's top word vanish). So the common case writes the header
; first and goes on in SubN's loop - nothing kept across it, all in registers; the rare case takes
; the full path (fi_subcmpn_hdr_full: the compare again, the zero words counted after).
;  Registers: rcx / rdx A / B (then the larger / the smaller), r8 R, r9 n, r10 i, r11 the
;  header, rax the top words' difference.
	align 32
ENTRY Fullint64SubCmpNHdrWIN
	WIN64_ARGS
	mov r11, r9                 ; the header
	mov r9d, [r9 + 4]           ; n
	xor r10d, r10d              ; i
.cmp:
	mov rax, [rcx + r10*8]
	sub rax, [rdx + r10*8]      ; the difference itself (CF: A[i] < B[i])
	jnz .diff
	inc r10
	cmp r10, r9
	jne .cmp
	mov qword [r8], 0           ; A == B: zero
	mov dword [r11], 0
	mov dword [r11 + 4], 1
	mov byte [r11 + 8], 0
	WRET
.diff:
	jc .swapped
	cmp rax, 1                  ; A[i] - B[i] = 1: the top word may vanish
	je .full
	jmp .ordered
.swapped:
	cmp rax, -1                 ; B[i] - A[i] = 1
	je .full
	xor byte [r11 + 8], 1       ; the sign: B's
	mov rax, rcx
	mov rcx, rdx                ; the larger
	mov rdx, rax                ; the smaller
.ordered:
	mov [r11], r10d             ; off = i
	mov eax, r9d
	sub eax, r10d
	mov [r11 + 4], eax          ; size = n - i
	lea rcx, [rcx + r10*8]
	lea rdx, [rdx + r10*8]
	lea r8, [r8 + r10*8]        ; the difference at its own places
	mov r9, rax                 ; n - i words
	jmp fi_subn_body
.full:
	mov r9, r11
fi_subcmpn_hdr_full:
	mov [rsp + 8], r9           ; the header (the rare case only)
	mov r9d, [r9 + 4]           ; n
	SUBCMPN EXIT_EQ_HDR, EXIT_HDR_SUB

; the file's code a multiple of 64 bytes: the kernels linked after it keep their alignment whatever
; changes here (a 200-byte change once cost addto 8 words 6.3 -> 9.5 ns through the layout alone)
	align 64, int3
