default rel
section .text

; =============================================================================
;  Number-theoretic transform kernels on 8 lanes of 32 bits (AVX2), for the multiplication
;  through three primes p < 2^30 (4p < 2^32). The values stay lazy in [0, 2p); a product with a
;  twiddle w (in Montgomery form, w·2^32 mod p, with wq = w·p^-1 mod 2^32 next to it) is
;
;    m = a·wq mod 2^32;  r = hi(a·w) - hi(m·p) = a·w·2^-32 mod p, in (-p, p);  r + p in [0, 2p)
;
;  (the low halves of a·w and m·p are equal: no borrow), for a < 4p, w < p. vpmuludq takes the
;  even lanes: the odd ones are shifted down, and the odd twiddles read 4 bytes further on (the
;  tables carry 8 spare entries at their end).
;
;  Twiddle tables: W[h + j] = (w_2h^j)·2^32 mod p for j < h (each level h at offset h).
;  Decimation in frequency (Dif1 / Dif2 / Dif3) takes natural order to a permuted one, the
;  decimation in time with the inverse tables (Dit3 / Dit1 / Dit2) takes it back. Dif3 / Dit3
;  run the levels 4, 2, 1 inside the registers on 16-value chunks and leave the chunks in
;  their own order (the pointwise product does not care).
;
;  Windows x64. Every kernel runs over a whole array of L values (L a multiple of 16, of 2h).
;    void Fullint64NttDif1WIN_avx2(uint32* x, int64 L, int64 h, const uint32* W, const uint32* Wq, uint32 p)
;    void Fullint64NttDif2WIN_avx2(... same ...)           levels h and h/2 in one pass (h >= 16)
;    void Fullint64NttDif3WIN_avx2(uint32* x, int64 L, const uint32* W, const uint32* Wq, uint32 p)
;    void Fullint64NttDit1WIN_avx2 / Dit2 (as Dif1 / Dif2, inverse tables; Dit2: levels h/2, h)
;    void Fullint64NttDit3WIN_avx2 (as Dif3, inverse tables)
;    void Fullint64NttR3DifWIN_avx2(uint32* x, int64 M, const uint32* k, uint32 p)
;    void Fullint64NttR3DitWIN_avx2(uint32* x, int64 M, const uint32* k, uint32 p)
;         the radix-3 level of a transform of N = 3M values (M a multiple of 8), see below
;    void Fullint64NttPointWIN_avx2(uint32* x, const uint32* y, int64 n, uint32 p, uint32 pinv)
;         x = x·y·2^-32 (n a multiple of 8)
;    void Fullint64NttScaleWIN_avx2(uint32* x, int64 n, uint32 p, uint32 c, uint32 cq)
;         x = x·c·2^-32 in [0, p)
;    void Fullint64NttLoadWIN_avx2(const uint64* a, int64 la, uint32* x, int64 L, uint32 p)
;         big-endian words -> little-endian 32-bit digits in [0, 2p), zeros up to L
;    void Fullint64NttCrtWIN(const uint32* x0, const uint32* x1, const uint32* x2, int64 nd,
;                            uint64* r, int64 len, const uint64* k)
;         Garner over the three residues of nd = 2·len digits into r (big-endian words);
;         k: p1, p2, p3, inv12, inv12s, p1m3, p1m3s, inv123, inv123s, p1p2 (s: Shoup factors);
;         returns the carry out of the top word (0 for a product; a cyclic one wraps it)
;
;  ymm15 = p, ymm14 = 2p in every kernel.
; =============================================================================

; dst = a·w·2^-32 mod p in [0, 2p).  %1 dst, %2 a (reg), %3 w (even lanes), %4 w odd lanes
; (in even positions), %5 wq, %6..%8 temps. dst, a and the temps distinct.
%macro MULW 8
	vpmulld   %6, %2, %5        ; m = a·wq mod 2^32
	vpmuludq  %7, %2, %3        ; a·w, even lanes
	vpsrlq    %8, %2, 32
	vpmuludq  %8, %8, %4        ; a·w, odd lanes
	vpmuludq  %1, %6, ymm15     ; m·p, even
	vpsubq    %7, %7, %1
	vpsrlq    %6, %6, 32
	vpmuludq  %6, %6, ymm15     ; m·p, odd
	vpsubq    %8, %8, %6
	vpsrlq    %7, %7, 32
	vpblendd  %1, %7, %8, 0xAA
	vpaddd    %1, %1, ymm15
%endmacro

; %1 = min(%1, %1 - 2p): [0, 4p) -> [0, 2p); %2 temp
%macro RED 2
	vpsubd    %2, %1, ymm14
	vpminud   %1, %1, %2
%endmacro

; xmm6..xmm15 are callee-saved (Windows x64): 160 bytes on the stack
%macro SAVE_XMM 0
	sub rsp, 168
	vmovdqu [rsp], xmm6
	vmovdqu [rsp + 16], xmm7
	vmovdqu [rsp + 32], xmm8
	vmovdqu [rsp + 48], xmm9
	vmovdqu [rsp + 64], xmm10
	vmovdqu [rsp + 80], xmm11
	vmovdqu [rsp + 96], xmm12
	vmovdqu [rsp + 112], xmm13
	vmovdqu [rsp + 128], xmm14
	vmovdqu [rsp + 144], xmm15
%endmacro
%macro RESTORE_XMM 0
	vmovdqu xmm6, [rsp]
	vmovdqu xmm7, [rsp + 16]
	vmovdqu xmm8, [rsp + 32]
	vmovdqu xmm9, [rsp + 48]
	vmovdqu xmm10, [rsp + 64]
	vmovdqu xmm11, [rsp + 80]
	vmovdqu xmm12, [rsp + 96]
	vmovdqu xmm13, [rsp + 112]
	vmovdqu xmm14, [rsp + 128]
	vmovdqu xmm15, [rsp + 144]
	add rsp, 168
%endmacro

; ymm15 = p, ymm14 = 2p from a 32-bit value in memory
%macro LOAD_P 1
	vpbroadcastd ymm15, %1
	vpaddd ymm14, ymm15, ymm15
%endmacro

; -----------------------------------------------------------------------------
; one DIF level h: (x, y = x + h) -> (x + y, (x - y)·w^j), blocks of 2h
global Fullint64NttDif1WIN_avx2
Fullint64NttDif1WIN_avx2:
	push rsi
	push rdi
	SAVE_XMM
	mov r10, [rsp + 168 + 16 + 40]  ; Wq
	LOAD_P dword [rsp + 168 + 16 + 48]
	lea r9, [r9 + r8*4]         ; W + h
	lea r10, [r10 + r8*4]       ; Wq + h
	shl r8, 2                   ; h in bytes
	lea rdx, [rcx + rdx*4]      ; the end of x
.block:
	lea rsi, [rcx + r8]         ; y
	xor eax, eax
.word:
	vmovdqu ymm0, [rcx + rax]
	vmovdqu ymm1, [rsi + rax]
	vpaddd ymm2, ymm0, ymm1
	RED ymm2, ymm3
	vmovdqu [rcx + rax], ymm2
	vpsubd ymm0, ymm0, ymm1
	vpaddd ymm0, ymm0, ymm14
	MULW ymm1, ymm0, [r9 + rax], [r9 + rax + 4], [r10 + rax], ymm3, ymm4, ymm5
	vmovdqu [rsi + rax], ymm1
	add rax, 32
	cmp rax, r8
	jb .word
	lea rcx, [rcx + r8*2]
	cmp rcx, rdx
	jb .block
	vzeroupper
	RESTORE_XMM
	pop rdi
	pop rsi
	ret

; -----------------------------------------------------------------------------
; two DIF levels h, h/2 in one pass over each block of 2h: four streams x0..x3 (q = h/2 apart)
;   level h:   (x0, x2) with w^j, (x1, x3) with w^(j+q)      (w = W + h)
;   level h/2: (x0, x1), (x2, x3) with v^j                   (v = W + q)
global Fullint64NttDif2WIN_avx2
Fullint64NttDif2WIN_avx2:
	push rbx
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	SAVE_XMM
	mov r10, [rsp + 168 + 56 + 40]  ; Wq
	LOAD_P dword [rsp + 168 + 56 + 48]
	mov r11, r8
	shr r11, 1                  ; q
	lea r12, [r9 + r11*4]       ; v = W + q
	lea r13, [r10 + r11*4]      ; vq
	lea r9, [r9 + r8*4]         ; w = W + h
	lea r10, [r10 + r8*4]       ; wq
	shl r11, 2                  ; q in bytes
	lea r14, [r9 + r11]         ; w + q
	lea r15, [r10 + r11]        ; wq + q
	lea rdx, [rcx + rdx*4]      ; the end of x
.block:
	lea rsi, [rcx + r11]        ; x1
	lea rdi, [rsi + r11]        ; x2
	lea rbx, [rdi + r11]        ; x3
	xor eax, eax
.word:
	vmovdqu ymm0, [rcx + rax]
	vmovdqu ymm1, [rsi + rax]
	vmovdqu ymm2, [rdi + rax]
	vmovdqu ymm3, [rbx + rax]
	vpaddd ymm4, ymm0, ymm2     ; s0
	RED ymm4, ymm5
	vpsubd ymm0, ymm0, ymm2
	vpaddd ymm0, ymm0, ymm14
	MULW ymm2, ymm0, [r9 + rax], [r9 + rax + 4], [r10 + rax], ymm5, ymm6, ymm7     ; d0
	vpaddd ymm5, ymm1, ymm3     ; s1
	RED ymm5, ymm6
	vpsubd ymm1, ymm1, ymm3
	vpaddd ymm1, ymm1, ymm14
	MULW ymm3, ymm1, [r14 + rax], [r14 + rax + 4], [r15 + rax], ymm6, ymm7, ymm8   ; d1
	vmovdqu ymm9, [r12 + rax]   ; v^j
	vmovdqu ymm10, [r12 + rax + 4]
	vmovdqu ymm11, [r13 + rax]
	vpaddd ymm0, ymm4, ymm5
	RED ymm0, ymm1
	vmovdqu [rcx + rax], ymm0
	vpsubd ymm4, ymm4, ymm5
	vpaddd ymm4, ymm4, ymm14
	MULW ymm1, ymm4, ymm9, ymm10, ymm11, ymm6, ymm7, ymm8
	vmovdqu [rsi + rax], ymm1
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vmovdqu [rdi + rax], ymm0
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm14
	MULW ymm1, ymm2, ymm9, ymm10, ymm11, ymm6, ymm7, ymm8
	vmovdqu [rbx + rax], ymm1
	add rax, 32
	cmp rax, r11
	jb .word
	lea rcx, [rbx + r11]        ; the next block
	cmp rcx, rdx
	jb .block
	vzeroupper
	RESTORE_XMM
	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	ret

; -----------------------------------------------------------------------------
; the last three DIF levels (h = 4, 2, 1) on 16-value chunks A, B (two blocks of 8):
;   X = A.lo|B.lo, Y = A.hi|B.hi        level 4 with w8 = W[4..7] in each half
;   P = unpacklo64, Q = unpackhi64      level 2 with w4 = W[2..3] repeated
;   R = s.even|d.even, S = s.odd|d.odd  level 1 (w = 1); stored as R, S
global Fullint64NttDif3WIN_avx2
Fullint64NttDif3WIN_avx2:
	SAVE_XMM
	LOAD_P dword [rsp + 168 + 40]
	vbroadcasti128 ymm8, [r8 + 16]      ; w8
	vbroadcasti128 ymm9, [r8 + 20]      ; w8, odd lanes in even positions
	vbroadcasti128 ymm10, [r9 + 16]     ; w8q
	vpbroadcastq ymm11, [r8 + 8]        ; w4
	vpbroadcastd ymm12, [r8 + 12]       ; w4, odd
	vpbroadcastq ymm13, [r9 + 8]        ; w4q
	lea rdx, [rcx + rdx*4]
.chunk:
	vmovdqu ymm0, [rcx]
	vmovdqu ymm1, [rcx + 32]
	vperm2i128 ymm2, ymm0, ymm1, 0x20   ; X
	vperm2i128 ymm3, ymm0, ymm1, 0x31   ; Y
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm14
	MULW ymm1, ymm2, ymm8, ymm9, ymm10, ymm3, ymm4, ymm5
	vpunpcklqdq ymm2, ymm0, ymm1        ; P
	vpunpckhqdq ymm3, ymm0, ymm1        ; Q
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm14
	MULW ymm1, ymm2, ymm11, ymm12, ymm13, ymm3, ymm4, ymm5
	vpsllq ymm2, ymm1, 32
	vpblendd ymm2, ymm0, ymm2, 0xAA     ; R
	vpsrlq ymm3, ymm0, 32
	vpblendd ymm3, ymm3, ymm1, 0xAA     ; S
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vmovdqu [rcx], ymm0
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm14
	RED ymm2, ymm1
	vmovdqu [rcx + 32], ymm2
	add rcx, 64
	cmp rcx, rdx
	jb .chunk
	vzeroupper
	RESTORE_XMM
	ret

; -----------------------------------------------------------------------------
; one DIT level h: (x, y) -> (x + y·w^j, x - y·w^j)
global Fullint64NttDit1WIN_avx2
Fullint64NttDit1WIN_avx2:
	push rsi
	push rdi
	SAVE_XMM
	mov r10, [rsp + 168 + 16 + 40]
	LOAD_P dword [rsp + 168 + 16 + 48]
	lea r9, [r9 + r8*4]
	lea r10, [r10 + r8*4]
	shl r8, 2
	lea rdx, [rcx + rdx*4]
.block:
	lea rsi, [rcx + r8]
	xor eax, eax
.word:
	vmovdqu ymm1, [rsi + rax]
	MULW ymm2, ymm1, [r9 + rax], [r9 + rax + 4], [r10 + rax], ymm3, ymm4, ymm5
	vmovdqu ymm0, [rcx + rax]
	vpaddd ymm1, ymm0, ymm2
	RED ymm1, ymm3
	vmovdqu [rcx + rax], ymm1
	vpsubd ymm0, ymm0, ymm2
	vpaddd ymm0, ymm0, ymm14
	RED ymm0, ymm3
	vmovdqu [rsi + rax], ymm0
	add rax, 32
	cmp rax, r8
	jb .word
	lea rcx, [rcx + r8*2]
	cmp rcx, rdx
	jb .block
	vzeroupper
	RESTORE_XMM
	pop rdi
	pop rsi
	ret

; -----------------------------------------------------------------------------
; two DIT levels h/2, h in one pass over each block of 2h (Dif2 backwards)
;   level h/2: (x0, x1), (x2, x3) with v^j
;   level h:   (x0, x2) with w^j, (x1, x3) with w^(j+q)
global Fullint64NttDit2WIN_avx2
Fullint64NttDit2WIN_avx2:
	push rbx
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	SAVE_XMM
	mov r10, [rsp + 168 + 56 + 40]
	LOAD_P dword [rsp + 168 + 56 + 48]
	mov r11, r8
	shr r11, 1
	lea r12, [r9 + r11*4]
	lea r13, [r10 + r11*4]
	lea r9, [r9 + r8*4]
	lea r10, [r10 + r8*4]
	shl r11, 2
	lea r14, [r9 + r11]
	lea r15, [r10 + r11]
	lea rdx, [rcx + rdx*4]
.block:
	lea rsi, [rcx + r11]
	lea rdi, [rsi + r11]
	lea rbx, [rdi + r11]
	xor eax, eax
.word:
	vmovdqu ymm9, [r12 + rax]
	vmovdqu ymm10, [r12 + rax + 4]
	vmovdqu ymm11, [r13 + rax]
	vmovdqu ymm1, [rsi + rax]
	MULW ymm12, ymm1, ymm9, ymm10, ymm11, ymm5, ymm6, ymm7     ; t1
	vmovdqu ymm3, [rbx + rax]
	MULW ymm13, ymm3, ymm9, ymm10, ymm11, ymm5, ymm6, ymm7     ; t3
	vmovdqu ymm0, [rcx + rax]
	vmovdqu ymm2, [rdi + rax]
	vpaddd ymm4, ymm0, ymm12    ; s0
	RED ymm4, ymm5
	vpsubd ymm0, ymm0, ymm12    ; d0
	vpaddd ymm0, ymm0, ymm14
	RED ymm0, ymm5
	vpaddd ymm1, ymm2, ymm13    ; s2
	RED ymm1, ymm5
	vpsubd ymm2, ymm2, ymm13    ; d2
	vpaddd ymm2, ymm2, ymm14
	RED ymm2, ymm5
	MULW ymm3, ymm1, [r9 + rax], [r9 + rax + 4], [r10 + rax], ymm5, ymm6, ymm7      ; u0
	MULW ymm1, ymm2, [r14 + rax], [r14 + rax + 4], [r15 + rax], ymm5, ymm6, ymm7    ; u1
	vpaddd ymm5, ymm4, ymm3
	RED ymm5, ymm6
	vmovdqu [rcx + rax], ymm5
	vpsubd ymm4, ymm4, ymm3
	vpaddd ymm4, ymm4, ymm14
	RED ymm4, ymm6
	vmovdqu [rdi + rax], ymm4
	vpaddd ymm5, ymm0, ymm1
	RED ymm5, ymm6
	vmovdqu [rsi + rax], ymm5
	vpsubd ymm0, ymm0, ymm1
	vpaddd ymm0, ymm0, ymm14
	RED ymm0, ymm6
	vmovdqu [rbx + rax], ymm0
	add rax, 32
	cmp rax, r11
	jb .word
	lea rcx, [rbx + r11]
	cmp rcx, rdx
	jb .block
	vzeroupper
	RESTORE_XMM
	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	ret

; -----------------------------------------------------------------------------
; the first three DIT levels (h = 1, 2, 4) on 16-value chunks: Dif3 backwards
global Fullint64NttDit3WIN_avx2
Fullint64NttDit3WIN_avx2:
	SAVE_XMM
	LOAD_P dword [rsp + 168 + 40]
	vbroadcasti128 ymm8, [r8 + 16]      ; i8
	vbroadcasti128 ymm9, [r8 + 20]
	vbroadcasti128 ymm10, [r9 + 16]
	vpbroadcastq ymm11, [r8 + 8]        ; i4
	vpbroadcastd ymm12, [r8 + 12]
	vpbroadcastq ymm13, [r9 + 8]
	lea rdx, [rcx + rdx*4]
.chunk:
	vmovdqu ymm2, [rcx]                 ; R
	vmovdqu ymm3, [rcx + 32]            ; S
	vpaddd ymm0, ymm2, ymm3             ; r
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3             ; s
	vpaddd ymm2, ymm2, ymm14
	RED ymm2, ymm1
	vpsllq ymm1, ymm2, 32
	vpblendd ymm3, ymm0, ymm1, 0xAA     ; P = r.even|s.even
	vpsrlq ymm0, ymm0, 32
	vpblendd ymm2, ymm0, ymm2, 0xAA     ; Q = r.odd|s.odd
	MULW ymm1, ymm2, ymm11, ymm12, ymm13, ymm0, ymm4, ymm5      ; t
	vpaddd ymm0, ymm3, ymm1             ; p1
	RED ymm0, ymm4
	vpsubd ymm3, ymm3, ymm1             ; q1
	vpaddd ymm3, ymm3, ymm14
	RED ymm3, ymm4
	vpunpcklqdq ymm2, ymm0, ymm3        ; X
	vpunpckhqdq ymm3, ymm0, ymm3        ; Y
	MULW ymm1, ymm3, ymm8, ymm9, ymm10, ymm0, ymm4, ymm5        ; t
	vpaddd ymm0, ymm2, ymm1             ; x1
	RED ymm0, ymm4
	vpsubd ymm2, ymm2, ymm1             ; y1
	vpaddd ymm2, ymm2, ymm14
	RED ymm2, ymm4
	vperm2i128 ymm1, ymm0, ymm2, 0x20
	vperm2i128 ymm3, ymm0, ymm2, 0x31
	vmovdqu [rcx], ymm1
	vmovdqu [rcx + 32], ymm3
	add rcx, 64
	cmp rcx, rdx
	jb .chunk
	vzeroupper
	RESTORE_XMM
	ret

; -----------------------------------------------------------------------------
; x = x·y·2^-32 mod p in [0, 2p)
global Fullint64NttPointWIN_avx2
Fullint64NttPointWIN_avx2:
	SAVE_XMM
	vmovd xmm15, r9d
	vpbroadcastd ymm15, xmm15
	vpaddd ymm14, ymm15, ymm15
	vpbroadcastd ymm13, dword [rsp + 168 + 40]  ; pinv
	shl r8, 2
	xor eax, eax
.word:
	vmovdqu ymm1, [rdx + rax]
	vpmulld ymm2, ymm1, ymm13           ; y·pinv
	vpsrlq ymm3, ymm1, 32
	vmovdqu ymm0, [rcx + rax]
	MULW ymm4, ymm0, ymm1, ymm3, ymm2, ymm5, ymm6, ymm7
	vmovdqu [rcx + rax], ymm4
	add rax, 32
	cmp rax, r8
	jb .word
	vzeroupper
	RESTORE_XMM
	ret

; -----------------------------------------------------------------------------
; x = x·c·2^-32 mod p in [0, p)
global Fullint64NttScaleWIN_avx2
Fullint64NttScaleWIN_avx2:
	SAVE_XMM
	vmovd xmm15, r8d
	vpbroadcastd ymm15, xmm15
	vpaddd ymm14, ymm15, ymm15
	vmovd xmm12, r9d
	vpbroadcastd ymm12, xmm12           ; c (odd lanes: the same)
	vpbroadcastd ymm13, dword [rsp + 168 + 40]  ; cq
	shl rdx, 2
	xor eax, eax
.word:
	vmovdqu ymm0, [rcx + rax]
	MULW ymm1, ymm0, ymm12, ymm12, ymm13, ymm2, ymm3, ymm4
	vpsubd ymm2, ymm1, ymm15
	vpminud ymm1, ymm1, ymm2
	vmovdqu [rcx + rax], ymm1
	add rax, 32
	cmp rax, rdx
	jb .word
	vzeroupper
	RESTORE_XMM
	ret

; -----------------------------------------------------------------------------
; big-endian words -> little-endian digits in [0, 2p), then zeros up to L
;   rcx a, rdx la, r8 x, r9 L, [p]
global Fullint64NttLoadWIN_avx2
Fullint64NttLoadWIN_avx2:
	push rsi
	push rdi
	mov eax, [rsp + 16 + 40]            ; p
	vmovd xmm5, eax
	vpbroadcastd ymm5, xmm5
	vpaddd ymm4, ymm5, ymm5             ; 2p
	vpaddd ymm3, ymm4, ymm4             ; 4p
	lea rsi, [rcx + rdx*8]              ; past the lowest word
	mov rdi, r8
	mov r10, rdx                        ; words left
	lea r11d, [rax + rax]               ; 2p
	lea r8d, [rax*4]                    ; 4p
.quad:
	cmp r10, 4
	jb .tail
	sub rsi, 32
	vmovdqu ymm0, [rsi]
	vpermq ymm0, ymm0, 0x1B             ; the four words low first: digits in order
	vpsubd ymm1, ymm0, ymm3
	vpminud ymm0, ymm0, ymm1            ; [0, 4p)
	vpsubd ymm1, ymm0, ymm4
	vpminud ymm0, ymm0, ymm1            ; [0, 2p)
	vmovdqu [rdi], ymm0
	add rdi, 32
	sub r10, 4
	jmp .quad
.tail:
	test r10, r10
	jz .zeros
	sub rsi, 8
	mov ecx, [rsi]                      ; low digit
	call .digit
	mov ecx, [rsi + 4]                  ; high digit
	call .digit
	dec r10
	jmp .tail
.zeros:
	vzeroupper
	mov rcx, r9
	sub rcx, rdx
	sub rcx, rdx                        ; L - 2·la digits
	xor eax, eax
	rep stosd
	pop rdi
	pop rsi
	ret
.digit:                                 ; ecx -> [0, 2p) at [rdi]
	mov eax, ecx
	sub eax, r8d
	cmovae ecx, eax
	mov eax, ecx
	sub eax, r11d
	cmovae ecx, eax
	mov [rdi], ecx
	add rdi, 4
	ret

; -----------------------------------------------------------------------------
; Garner: v1 = x0, v2 = (x1 - v1)·p1^-1 mod p2, v3 = (x2 - v1 - v2·p1)·(p1p2)^-1 mod p3;
; the digit's value v1 + p1·v2 + p1p2·v3 (< 2^90) added into the running sum, its low 32 bits
; out. Two digits make a word, stored from the low end of r up.
;   rcx x0, rdx x1, r8 x2, r9 nd, [r], [len], [k]
global Fullint64NttCrtWIN
Fullint64NttCrtWIN:
	push rbx
	push rbp
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	mov [rsp + 64 + 32], r9             ; nd (r9's home)
	mov r10, [rsp + 64 + 40]            ; r
	mov rax, [rsp + 64 + 48]            ; len
	lea r10, [r10 + rax*8 - 8]          ; its lowest word
	mov rdi, [rsp + 64 + 56]            ; k
	mov r11, rdx                        ; x1
	mov r12, [rdi + 8]                  ; p2
	mov r13, [rdi + 16]                 ; p3
	mov r14, [rdi + 72]                 ; p1p2
	xor ebx, ebx                        ; the running sum, low
	xor ebp, ebp                        ; high
	xor esi, esi                        ; i
%macro CRT_DIGIT 1
	mov eax, [rcx + rsi*4]              ; v1
	mov r9d, eax
	sub r9d, r12d
	cmovb r9d, eax                      ; v1 mod p2
	mov r15d, [r11 + rsi*4]
	add r15d, r12d
	sub r15d, r9d                       ; t = x1 + p2 - v1 (< 2p2)
	mov r9, r15
	imul r9, [rdi + 32]                 ; t·inv12s
	shr r9, 32
	imul r9d, r12d
	imul r15d, [rdi + 24]
	sub r15d, r9d                       ; v2 in [0, 2p2)
	mov r9d, r15d
	sub r9d, r12d
	cmovae r15d, r9d                    ; v2 < p2
	mov r9, r15
	imul r9, [rdi + 48]                 ; v2·p1m3s
	shr r9, 32
	imul r9d, r13d
	mov edx, r15d
	imul edx, [rdi + 40]
	sub edx, r9d                        ; v2·p1 mod p3 in [0, 2p3)
	mov r9d, eax
	sub r9d, r13d
	cmovb r9d, eax                      ; v1 mod p3
	add edx, r9d                        ; sp < 3p3
	lea r9d, [r13 + r13]
	neg r9d
	add r9d, edx                        ; sp - 2p3, CF: sp >= 2p3
	cmovc edx, r9d
	mov r9d, edx
	sub r9d, r13d
	cmovae edx, r9d                     ; sp < p3
	mov r9d, [r8 + rsi*4]
	add r9d, r13d
	sub r9d, edx                        ; t3 = x2 + p3 - sp (< 2p3)
	mov rdx, r9
	imul rdx, [rdi + 64]                ; t3·inv123s
	shr rdx, 32
	imul edx, r13d
	imul r9d, [rdi + 56]
	sub r9d, edx                        ; v3 in [0, 2p3)
	mov edx, r9d
	sub edx, r13d
	cmovae r9d, edx                     ; v3 < p3
	imul r15, [rdi]                     ; p1·v2
	add r15, rax                        ; + v1 (< 2^60)
	mov rax, r14
	mul r9                              ; p1p2·v3
	add rax, r15
	adc rdx, 0
	add rbx, rax
	adc rbp, rdx                        ; the running sum
	mov [r10 + %1], ebx                 ; the digit
	shrd rbx, rbp, 32
	shr rbp, 32
	inc rsi
%endmacro
.pair:
	CRT_DIGIT 0
	CRT_DIGIT 4
	sub r10, 8
	cmp rsi, [rsp + 64 + 32]
	jb .pair
	mov rax, rbx                        ; the carry out (below 2^64)
	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbp
	pop rbx
	ret

; -----------------------------------------------------------------------------
; The radix-3 level on top of a transform of N = 3M values (then three of M = 2^k: the lengths
; 3·2^k fill the gaps between the powers of two). w = w_N, ω = w^M (a cube root of unity,
; ω² = -1 - ω). Decimation in frequency, for j < M with x0, x1, x2 = x[j], x[j + M], x[j + 2M]:
;   y0 = x0 + x1 + x2
;   y1 = (x0 + ω·x1 + ω²·x2)·w^j   = (x0 - x2 + ω·(x1 - x2))·w^j
;   y2 = (x0 + ω²·x1 + ω·x2)·w^2j  = (x0 - x1 - ω·(x1 - x2))·w^2j
; and in time (the inverse, w and ω inverted): y1' = y1·w^j, y2' = y2·w^2j,
;   x0 = y0 + y1' + y2',  x1 = y0 - y2' + ω·(y1' - y2'),  x2 = y0 - y1' - ω·(y1' - y2')
; The twiddles w^j, w^2j are made as the loop goes (times w^8, w^16 a pass, kept below p):
; no tables. k: nine vectors of 8 values (Montgomery form, ·2^32 mod p; q = ·p^-1 mod 2^32):
;   [0] w^0..7, [1] w^0,2,..,14, [2] w^8, [3] its q, [4] w^16, [5] its q, [6] ω, [7] its q,
;   [8] p^-1 (all lanes)
;   rcx x, rdx M, r8 k, r9 p
%macro R3_SETUP 0
	SAVE_XMM
	vmovd xmm15, r9d
	vpbroadcastd ymm15, xmm15
	vpaddd ymm14, ymm15, ymm15
	vmovdqu ymm13, [r8 + 256]           ; p^-1
	vmovdqu ymm12, [r8]                 ; w^j
	vmovdqu ymm11, [r8 + 32]            ; w^2j
	lea r10, [rcx + rdx*4]              ; x1
	lea r11, [r10 + rdx*4]              ; x2
	shl rdx, 2
	xor eax, eax
%endmacro

; the twiddles one pass on: w^j·w^8, w^2j·w^16, reduced below p
%macro R3_ADVANCE 0
	MULW ymm3, ymm12, [r8 + 64], [r8 + 64], [r8 + 96], ymm5, ymm6, ymm7
	vpsubd ymm4, ymm3, ymm15
	vpminud ymm12, ymm3, ymm4
	MULW ymm3, ymm11, [r8 + 128], [r8 + 128], [r8 + 160], ymm5, ymm6, ymm7
	vpsubd ymm4, ymm3, ymm15
	vpminud ymm11, ymm3, ymm4
%endmacro

global Fullint64NttR3DifWIN_avx2
Fullint64NttR3DifWIN_avx2:
	R3_SETUP
.word:
	vmovdqu ymm0, [rcx + rax]
	vmovdqu ymm1, [r10 + rax]
	vmovdqu ymm2, [r11 + rax]
	vpaddd ymm3, ymm1, ymm2
	RED ymm3, ymm4                      ; x1 + x2
	vpaddd ymm3, ymm0, ymm3
	RED ymm3, ymm4
	vmovdqu [rcx + rax], ymm3           ; y0
	vpsubd ymm3, ymm1, ymm2
	vpaddd ymm3, ymm3, ymm14            ; x1 - x2
	MULW ymm4, ymm3, [r8 + 192], [r8 + 192], [r8 + 224], ymm5, ymm6, ymm7   ; r = ω·(x1 - x2)
	vpsubd ymm3, ymm0, ymm2
	vpaddd ymm3, ymm3, ymm14
	RED ymm3, ymm5
	vpaddd ymm3, ymm3, ymm4             ; A = x0 - x2 + r (< 4p)
	vpsubd ymm0, ymm0, ymm1
	vpaddd ymm0, ymm0, ymm14
	RED ymm0, ymm5
	vpsubd ymm0, ymm0, ymm4
	vpaddd ymm0, ymm0, ymm14            ; B = x0 - x1 - r (< 4p)
	vpmulld ymm8, ymm12, ymm13
	vpsrlq ymm9, ymm12, 32
	MULW ymm1, ymm3, ymm12, ymm9, ymm8, ymm5, ymm6, ymm7
	vmovdqu [r10 + rax], ymm1           ; y1 = A·w^j
	vpmulld ymm8, ymm11, ymm13
	vpsrlq ymm9, ymm11, 32
	MULW ymm2, ymm0, ymm11, ymm9, ymm8, ymm5, ymm6, ymm7
	vmovdqu [r11 + rax], ymm2           ; y2 = B·w^2j
	R3_ADVANCE
	add rax, 32
	cmp rax, rdx
	jb .word
	vzeroupper
	RESTORE_XMM
	ret

global Fullint64NttR3DitWIN_avx2
Fullint64NttR3DitWIN_avx2:
	R3_SETUP
.word:
	vmovdqu ymm0, [rcx + rax]
	vmovdqu ymm1, [r10 + rax]
	vmovdqu ymm2, [r11 + rax]
	vpmulld ymm8, ymm12, ymm13
	vpsrlq ymm9, ymm12, 32
	MULW ymm3, ymm1, ymm12, ymm9, ymm8, ymm5, ymm6, ymm7     ; y1' = y1·w^j
	vpmulld ymm8, ymm11, ymm13
	vpsrlq ymm9, ymm11, 32
	MULW ymm4, ymm2, ymm11, ymm9, ymm8, ymm5, ymm6, ymm7     ; y2' = y2·w^2j
	vpaddd ymm1, ymm3, ymm4
	RED ymm1, ymm5
	vpaddd ymm1, ymm0, ymm1
	RED ymm1, ymm5
	vmovdqu [rcx + rax], ymm1           ; x0
	vpsubd ymm1, ymm3, ymm4
	vpaddd ymm1, ymm1, ymm14
	MULW ymm2, ymm1, [r8 + 192], [r8 + 192], [r8 + 224], ymm5, ymm6, ymm7   ; r = ω·(y1' - y2')
	vpsubd ymm1, ymm0, ymm4
	vpaddd ymm1, ymm1, ymm14
	RED ymm1, ymm5
	vpaddd ymm1, ymm1, ymm2
	RED ymm1, ymm5
	vmovdqu [r10 + rax], ymm1           ; x1 = y0 - y2' + r
	vpsubd ymm1, ymm0, ymm3
	vpaddd ymm1, ymm1, ymm14
	RED ymm1, ymm5
	vpsubd ymm1, ymm1, ymm2
	vpaddd ymm1, ymm1, ymm14
	RED ymm1, ymm5
	vmovdqu [r11 + rax], ymm1           ; x2 = y0 - y1' - r
	R3_ADVANCE
	add rax, 32
	cmp rax, rdx
	jb .word
	vzeroupper
	RESTORE_XMM
	ret
