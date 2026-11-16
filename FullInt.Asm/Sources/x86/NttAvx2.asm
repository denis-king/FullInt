; =============================================================================
;  x86 (32-bit): the number-theoretic transform kernels of x64/NttAvx2.asm (AVX2, 8 lanes of
;  32 bits), the same functions, contracts and prototypes (cdecl; an int64 argument takes two
;  stack slots, only its low half is read). The scheme is described there.
;
;  32-bit mode has ymm0..ymm7: ymm7 = p, ymm6 = 2p, five registers for the work. So:
;    Dif2 / Dit2 are two single-level passes (Dif1 / Dit1) instead of one fused pass;
;    Dif3 / Dit3 keep their six twiddle vectors in the stack frame (memory operands);
;    the CRT works in 32-bit multiplies with a 96-bit running sum on the stack.
; =============================================================================

section .text

; dst = a·w·2^-32 mod p in [0, 2p) (x64/NttAvx2.asm): %1 dst, %2 a (reg), %3 w, %4 w odd lanes
; (in even positions), %5 wq, %6..%8 temps; %6 may be %5's register (read first)
%macro MULW 8
	vpmulld   %6, %2, %5
	vpmuludq  %7, %2, %3
	vpsrlq    %8, %2, 32
	vpmuludq  %8, %8, %4
	vpmuludq  %1, %6, ymm7
	vpsubq    %7, %7, %1
	vpsrlq    %6, %6, 32
	vpmuludq  %6, %6, ymm7
	vpsubq    %8, %8, %6
	vpsrlq    %7, %7, 32
	vpblendd  %1, %7, %8, 0xAA
	vpaddd    %1, %1, ymm7
%endmacro

%macro RED 2
	vpsubd    %2, %1, ymm6
	vpminud   %1, %1, %2
%endmacro

; -----------------------------------------------------------------------------
; void Fullint64NttDif1WIN_avx2(uint32* x, int64 L, int64 h, const uint32* W, const uint32* Wq, uint32 p)
;   args after 4 pushes: x +20, L +24, h +32, W +40, Wq +44, p +48
global Fullint64NttDif1WIN_avx2
Fullint64NttDif1WIN_avx2:
	push ebp
	push ebx
	push esi
	push edi
	mov ecx, [esp + 20]
	mov edx, [esp + 24]
	mov ebx, [esp + 32]         ; h
	mov esi, [esp + 40]
	mov edi, [esp + 44]
	vpbroadcastd ymm7, [esp + 48]
	vpaddd ymm6, ymm7, ymm7
	lea esi, [esi + ebx*4]      ; W + h
	lea edi, [edi + ebx*4]      ; Wq + h
	shl ebx, 2
	lea edx, [ecx + edx*4]      ; the end of x
.block:
	lea ebp, [ecx + ebx]        ; y
	xor eax, eax
.word:
	vmovdqu ymm0, [ecx + eax]
	vmovdqu ymm1, [ebp + eax]
	vpaddd ymm2, ymm0, ymm1
	RED ymm2, ymm3
	vmovdqu [ecx + eax], ymm2
	vpsubd ymm0, ymm0, ymm1
	vpaddd ymm0, ymm0, ymm6
	MULW ymm1, ymm0, [esi + eax], [esi + eax + 4], [edi + eax], ymm2, ymm3, ymm4
	vmovdqu [ebp + eax], ymm1
	add eax, 32
	cmp eax, ebx
	jb .word
	lea ecx, [ecx + ebx*2]
	cmp ecx, edx
	jb .block
	vzeroupper
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; -----------------------------------------------------------------------------
; one DIT level: (x, y) -> (x + y·w^j, x - y·w^j); the same arguments
global Fullint64NttDit1WIN_avx2
Fullint64NttDit1WIN_avx2:
	push ebp
	push ebx
	push esi
	push edi
	mov ecx, [esp + 20]
	mov edx, [esp + 24]
	mov ebx, [esp + 32]
	mov esi, [esp + 40]
	mov edi, [esp + 44]
	vpbroadcastd ymm7, [esp + 48]
	vpaddd ymm6, ymm7, ymm7
	lea esi, [esi + ebx*4]
	lea edi, [edi + ebx*4]
	shl ebx, 2
	lea edx, [ecx + edx*4]
.block:
	lea ebp, [ecx + ebx]
	xor eax, eax
.word:
	vmovdqu ymm1, [ebp + eax]
	MULW ymm2, ymm1, [esi + eax], [esi + eax + 4], [edi + eax], ymm3, ymm4, ymm5
	vmovdqu ymm0, [ecx + eax]
	vpaddd ymm1, ymm0, ymm2
	RED ymm1, ymm3
	vmovdqu [ecx + eax], ymm1
	vpsubd ymm0, ymm0, ymm2
	vpaddd ymm0, ymm0, ymm6
	RED ymm0, ymm3
	vmovdqu [ebp + eax], ymm0
	add eax, 32
	cmp eax, ebx
	jb .word
	lea ecx, [ecx + ebx*2]
	cmp ecx, edx
	jb .block
	vzeroupper
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; -----------------------------------------------------------------------------
; two levels as two passes: Dif2 = level h, then h/2; Dit2 = level h/2, then h. The arguments
; are copied for the calls (x, L, h, 0, W, Wq, p), the copy of h changed in between.
;   after push ebp / mov ebp, esp: x +8, L +12 / +16, h +20, W +28, Wq +32, p +36
%macro PASS_ARGS 0
	push dword [ebp + 36]
	push dword [ebp + 32]
	push dword [ebp + 28]
	push dword 0
	push dword [ebp + 20]
	push dword [ebp + 16]
	push dword [ebp + 12]
	push dword [ebp + 8]
%endmacro

global Fullint64NttDif2WIN_avx2
Fullint64NttDif2WIN_avx2:
	push ebp
	mov ebp, esp
	PASS_ARGS
	call Fullint64NttDif1WIN_avx2
	shr dword [esp + 12], 1     ; h / 2
	call Fullint64NttDif1WIN_avx2
	add esp, 32
	pop ebp
	ret

global Fullint64NttDit2WIN_avx2
Fullint64NttDit2WIN_avx2:
	push ebp
	mov ebp, esp
	PASS_ARGS
	shr dword [esp + 12], 1     ; h / 2 first
	call Fullint64NttDit1WIN_avx2
	shl dword [esp + 12], 1
	call Fullint64NttDit1WIN_avx2
	add esp, 32
	pop ebp
	ret

; -----------------------------------------------------------------------------
; the last three DIF levels on 16-value chunks (x64/NttAvx2.asm), the twiddle vectors in the frame
;   void Fullint64NttDif3WIN_avx2(uint32* x, int64 L, const uint32* W, const uint32* Wq, uint32 p)
;   after 4 pushes and the 192-byte frame: x +212, L +216, W +224, Wq +228, p +232
%define T_W8    esp
%define T_W8O   esp+32
%define T_W8Q   esp+64
%define T_W4    esp+96
%define T_W4O   esp+128
%define T_W4Q   esp+160
%macro TWIDDLE_FRAME 0
	mov esi, [esp + 224]
	mov edi, [esp + 228]
	vbroadcasti128 ymm0, [esi + 16]
	vmovdqu [T_W8], ymm0
	vbroadcasti128 ymm0, [esi + 20]
	vmovdqu [T_W8O], ymm0
	vbroadcasti128 ymm0, [edi + 16]
	vmovdqu [T_W8Q], ymm0
	vpbroadcastq ymm0, [esi + 8]
	vmovdqu [T_W4], ymm0
	vpbroadcastd ymm0, [esi + 12]
	vmovdqu [T_W4O], ymm0
	vpbroadcastq ymm0, [edi + 8]
	vmovdqu [T_W4Q], ymm0
	vpbroadcastd ymm7, [esp + 232]
	vpaddd ymm6, ymm7, ymm7
	mov ecx, [esp + 212]
	mov edx, [esp + 216]
	lea edx, [ecx + edx*4]
%endmacro

global Fullint64NttDif3WIN_avx2
Fullint64NttDif3WIN_avx2:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 192
	TWIDDLE_FRAME
.chunk:
	vmovdqu ymm0, [ecx]
	vmovdqu ymm1, [ecx + 32]
	vperm2i128 ymm2, ymm0, ymm1, 0x20
	vperm2i128 ymm3, ymm0, ymm1, 0x31
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm6
	MULW ymm1, ymm2, [T_W8], [T_W8O], [T_W8Q], ymm3, ymm4, ymm5
	vpunpcklqdq ymm2, ymm0, ymm1
	vpunpckhqdq ymm3, ymm0, ymm1
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm6
	MULW ymm1, ymm2, [T_W4], [T_W4O], [T_W4Q], ymm3, ymm4, ymm5
	vpsllq ymm2, ymm1, 32
	vpblendd ymm2, ymm0, ymm2, 0xAA
	vpsrlq ymm3, ymm0, 32
	vpblendd ymm3, ymm3, ymm1, 0xAA
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vmovdqu [ecx], ymm0
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm6
	RED ymm2, ymm1
	vmovdqu [ecx + 32], ymm2
	add ecx, 64
	cmp ecx, edx
	jb .chunk
	vzeroupper
	add esp, 192
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

global Fullint64NttDit3WIN_avx2
Fullint64NttDit3WIN_avx2:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 192
	TWIDDLE_FRAME
.chunk:
	vmovdqu ymm2, [ecx]
	vmovdqu ymm3, [ecx + 32]
	vpaddd ymm0, ymm2, ymm3
	RED ymm0, ymm1
	vpsubd ymm2, ymm2, ymm3
	vpaddd ymm2, ymm2, ymm6
	RED ymm2, ymm1
	vpsllq ymm1, ymm2, 32
	vpblendd ymm3, ymm0, ymm1, 0xAA
	vpsrlq ymm0, ymm0, 32
	vpblendd ymm2, ymm0, ymm2, 0xAA
	MULW ymm1, ymm2, [T_W4], [T_W4O], [T_W4Q], ymm0, ymm4, ymm5
	vpaddd ymm0, ymm3, ymm1
	RED ymm0, ymm4
	vpsubd ymm3, ymm3, ymm1
	vpaddd ymm3, ymm3, ymm6
	RED ymm3, ymm4
	vpunpcklqdq ymm2, ymm0, ymm3
	vpunpckhqdq ymm3, ymm0, ymm3
	MULW ymm1, ymm3, [T_W8], [T_W8O], [T_W8Q], ymm0, ymm4, ymm5
	vpaddd ymm0, ymm2, ymm1
	RED ymm0, ymm4
	vpsubd ymm2, ymm2, ymm1
	vpaddd ymm2, ymm2, ymm6
	RED ymm2, ymm4
	vperm2i128 ymm1, ymm0, ymm2, 0x20
	vperm2i128 ymm3, ymm0, ymm2, 0x31
	vmovdqu [ecx], ymm1
	vmovdqu [ecx + 32], ymm3
	add ecx, 64
	cmp ecx, edx
	jb .chunk
	vzeroupper
	add esp, 192
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; -----------------------------------------------------------------------------
; void Fullint64NttPointWIN_avx2(uint32* x, const uint32* y, int64 n, uint32 p, uint32 pinv)
;   x +4, y +8, n +12, p +20, pinv +24
global Fullint64NttPointWIN_avx2
Fullint64NttPointWIN_avx2:
	mov ecx, [esp + 4]
	mov edx, [esp + 8]
	vpbroadcastd ymm7, [esp + 20]
	mov eax, [esp + 12]
	shl eax, 2
	push ebx
	mov ebx, eax                ; n in bytes
	xor eax, eax
.word:
	vmovdqu ymm1, [edx + eax]
	vpbroadcastd ymm2, [esp + 4 + 24]
	vpmulld ymm2, ymm1, ymm2    ; y·pinv
	vpsrlq ymm3, ymm1, 32
	vmovdqu ymm0, [ecx + eax]
	MULW ymm4, ymm0, ymm1, ymm3, ymm2, ymm2, ymm5, ymm6
	vmovdqu [ecx + eax], ymm4
	add eax, 32
	cmp eax, ebx
	jb .word
	pop ebx
	vzeroupper
	ret

; -----------------------------------------------------------------------------
; void Fullint64NttScaleWIN_avx2(uint32* x, int64 n, uint32 p, uint32 c, uint32 cq)
;   x +4, n +8, p +16, c +20, cq +24; x = x·c·2^-32 mod p in [0, p)
global Fullint64NttScaleWIN_avx2
Fullint64NttScaleWIN_avx2:
	mov ecx, [esp + 4]
	mov edx, [esp + 8]
	shl edx, 2
	vpbroadcastd ymm7, [esp + 16]
	vpbroadcastd ymm4, [esp + 20]   ; c (odd lanes the same)
	vpbroadcastd ymm5, [esp + 24]   ; cq
	xor eax, eax
.word:
	vmovdqu ymm0, [ecx + eax]
	MULW ymm1, ymm0, ymm4, ymm4, ymm5, ymm2, ymm3, ymm6
	vpsubd ymm2, ymm1, ymm7
	vpminud ymm1, ymm1, ymm2
	vmovdqu [ecx + eax], ymm1
	add eax, 32
	cmp eax, edx
	jb .word
	vzeroupper
	ret

; -----------------------------------------------------------------------------
; void Fullint64NttLoadWIN_avx2(const uint64* a, int64 la, uint32* x, int64 L, uint32 p)
;   after 4 pushes: a +20, la +24, x +32, L +36, p +44
global Fullint64NttLoadWIN_avx2
Fullint64NttLoadWIN_avx2:
	push ebp
	push ebx
	push esi
	push edi
	mov eax, [esp + 44]         ; p
	vmovd xmm5, eax
	vpbroadcastd ymm5, xmm5
	vpaddd ymm4, ymm5, ymm5     ; 2p
	vpaddd ymm3, ymm4, ymm4     ; 4p
	lea ebx, [eax + eax]        ; 2p
	lea ebp, [eax*4]            ; 4p
	mov ecx, [esp + 24]         ; la
	mov esi, [esp + 20]
	lea esi, [esi + ecx*8]      ; past the lowest word
	mov edi, [esp + 32]
.quad:
	cmp ecx, 4
	jb .tail
	sub esi, 32
	vmovdqu ymm0, [esi]
	vpermq ymm0, ymm0, 0x1B
	vpsubd ymm1, ymm0, ymm3
	vpminud ymm0, ymm0, ymm1
	vpsubd ymm1, ymm0, ymm4
	vpminud ymm0, ymm0, ymm1
	vmovdqu [edi], ymm0
	add edi, 32
	sub ecx, 4
	jmp .quad
.tail:
	test ecx, ecx
	jz .zeros
	sub esi, 8
	mov eax, [esi]              ; the low digit
	mov edx, eax
	sub edx, ebp
	cmovae eax, edx
	mov edx, eax
	sub edx, ebx
	cmovae eax, edx
	mov [edi], eax
	mov eax, [esi + 4]          ; the high digit
	mov edx, eax
	sub edx, ebp
	cmovae eax, edx
	mov edx, eax
	sub edx, ebx
	cmovae eax, edx
	mov [edi + 4], eax
	add edi, 8
	dec ecx
	jmp .tail
.zeros:
	vzeroupper
	mov ecx, [esp + 36]         ; L
	sub ecx, [esp + 24]
	sub ecx, [esp + 24]         ; L - 2·la digits
	xor eax, eax
	rep stosd
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

; -----------------------------------------------------------------------------
; uint64 Fullint64NttCrtWIN(const uint32* x0, const uint32* x1, const uint32* x2, int64 nd,
;                           uint64* r, int64 len, const uint64* k)
; Garner as x64/NttAvx2.asm, in 32-bit multiplies:
;   value = v1 + p1·u, u = v2 + p2·v3 (< 2^60): p1·u_lo + p1·u_hi·2^32, into the 96-bit sum
; Returns the carry out of the top word (edx:eax).
;   after 4 pushes and 16 bytes of locals: x0 +36, x1 +40, x2 +44, nd +48, r +56, len +60, k +68
%define S0      esp             ; the running sum, three dwords
%define S1      esp+4
%define S2      esp+8
%define V1      esp+12          ; v1 of the digit
%define A_X0    esp+36
%define A_X1    esp+40
%define A_X2    esp+44
%define A_ND    esp+48
%define A_R     esp+56
%define A_LEN   esp+60
%define A_K     esp+68
%define K_P1    ebp
%define K_P2    ebp+8
%define K_P3    ebp+16
%define K_I12   ebp+24
%define K_I12S  ebp+32
%define K_P1M3  ebp+40
%define K_P1M3S ebp+48
%define K_I123  ebp+56
%define K_I123S ebp+64

%macro CRT_DIGIT 1
	mov ebx, [A_X0]
	mov ecx, [ebx + esi*4]      ; v1
	mov [V1], ecx
	mov eax, ecx
	sub eax, [K_P2]
	cmovb eax, ecx              ; v1 mod p2
	mov ebx, [A_X1]
	mov ebx, [ebx + esi*4]
	add ebx, [K_P2]
	sub ebx, eax                ; t = x1 + p2 - v1 (< 2p2)
	mov eax, ebx
	mul dword [K_I12S]          ; edx = q
	imul edx, [K_P2]
	imul ebx, [K_I12]
	sub ebx, edx                ; [0, 2p2)
	mov eax, ebx
	sub eax, [K_P2]
	cmovae ebx, eax             ; v2 (ebx)
	mov eax, ebx
	mul dword [K_P1M3S]
	imul edx, [K_P3]
	mov eax, ebx
	imul eax, [K_P1M3]
	sub eax, edx                ; v2·p1 mod p3 in [0, 2p3)
	mov edx, ecx
	sub edx, [K_P3]
	cmovb edx, ecx              ; v1 mod p3
	add eax, edx                ; sp < 3p3
	mov edx, [K_P3]
	add edx, edx
	neg edx
	add edx, eax                ; sp - 2p3, CF: sp >= 2p3
	cmovc eax, edx
	mov edx, eax
	sub edx, [K_P3]
	cmovae eax, edx             ; sp < p3
	mov edx, [A_X2]
	mov edx, [edx + esi*4]
	add edx, [K_P3]
	sub edx, eax                ; t3 = x2 + p3 - sp (< 2p3)
	mov ecx, edx
	mov eax, edx
	mul dword [K_I123S]
	imul edx, [K_P3]
	imul ecx, [K_I123]
	sub ecx, edx
	mov eax, ecx
	sub eax, [K_P3]
	cmovae ecx, eax             ; v3 (ecx)
	mov eax, ecx
	mul dword [K_P2]
	add eax, ebx
	adc edx, 0                  ; u = p2·v3 + v2 (edx:eax)
	mov ecx, edx                ; u_hi
	mul dword [K_P1]            ; p1·u_lo
	add eax, [V1]
	adc edx, 0
	add [S0], eax
	adc [S1], edx
	adc dword [S2], 0
	mov eax, ecx
	mul dword [K_P1]            ; p1·u_hi
	add [S1], eax
	adc [S2], edx
	mov eax, [S0]
	mov [edi + %1], eax         ; the digit
	mov eax, [S1]
	mov [S0], eax
	mov eax, [S2]
	mov [S1], eax
	mov dword [S2], 0
	inc esi
%endmacro

global Fullint64NttCrtWIN
Fullint64NttCrtWIN:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, 16
	mov ebp, [A_K]
	mov edi, [A_R]
	mov eax, [A_LEN]
	lea edi, [edi + eax*8 - 8]  ; the lowest word of r
	xor esi, esi
	mov dword [S0], 0
	mov dword [S1], 0
	mov dword [S2], 0
.pair:
	CRT_DIGIT 0
	CRT_DIGIT 4
	sub edi, 8
	cmp esi, [A_ND]
	jb .pair
	mov eax, [S0]
	mov edx, [S1]
	add esp, 16
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
