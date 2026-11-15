
default rel
section .text

; =============================================================================
;  Magnitude shifts with AVX2 (Haswell, Zen and later; OS ymm support).
;  New kernels next to Shift.asm (the baseline, kept as is); picked at start-up when
;  Fullint64CpuFeatures reports AVX2. Same contracts (index 0 = most significant word):
;
;    void Fullint64ShlWIN_avx2(A, R, int lenA, int lenR, int shift)
;      lenR >= lenA + ws + 1;  R = [lead zeros][A shifted: lenA + 1 words][ws zeros]
;    void Fullint64ShrWIN_avx2(A, R, int lenA, int lenR, int shift)
;      lenR >= max(lenA - ws, 1);  R = [lead zeros][A[0 .. lenA-ws-1] shifted right by s];
;      R may alias A
;    shift = ws * 64 + s
;
;  Every result word comes from two neighbouring words of A, so four at a time:
;    Shl:  R[lead + k] = (A[k-1] << s) | (A[k] >> (64 - s))      (A[-1] = A[lenA] = 0)
;    Shr:  R[lead + k] = (A[k] >> s)   | (A[k-1] << (64 - s))    (A[-1] = 0)
;  the neighbour vector is a second unaligned load one word over. vpsllq / vpsrlq give 0 for
;  a count of 64, so s = 0 needs no branch; single words go through xmm the same way.
;
;  Shr walks from the low end and reads both vectors before storing, and writes its lead
;  zeros last: R may be A, with any lead.
;
;  Registers (Win64 volatile only): rcx A, rdx R, r8 lenA, r9 lenR, r10 ws, r11 words left,
;  rax scratch; xmm4 = s, xmm5 = 64 - s, ymm0 / ymm1 / ymm3 work. vzeroupper on return.
; =============================================================================

; zero %2 words at [%1], upwards; %1, %2 are advanced / used up (ymm3 = 0)
%macro ZERO_UP 2
%%blocks:
	cmp %2, 4
	jb %%words
	vmovdqu [%1], ymm3
	add %1, 32
	sub %2, 4
	jmp %%blocks
%%words:
	test %2, %2
	jz %%done
	mov qword [%1], 0
	add %1, 8
	dec %2
	jmp %%words
%%done:
%endmacro

global Fullint64ShlWIN_avx2
Fullint64ShlWIN_avx2:
	movsxd r8, r8d              ; lenA
	movsxd r9, r9d              ; lenR
	mov eax, [rsp+28h]          ; shift
	mov r10, rax
	shr r10, 6                  ; ws
	and eax, 63                 ; s
	vmovq xmm4, rax
	mov r11d, 64
	sub r11, rax
	vmovq xmm5, r11             ; 64 - s
	vpxor ymm3, ymm3, ymm3

	; lead zeros: lenR - lenA - 1 - ws words at R's top
	mov r11, r9
	sub r11, r8
	sub r11, 1
	sub r11, r10
	ZERO_UP rdx, r11

	; k = 0: the top word, A[0] >> (64 - s)
	vmovq xmm0, [rcx]
	vpsrlq xmm0, xmm0, xmm5
	vmovq [rdx], xmm0
	add rdx, 8

	; k = 1 .. lenA - 1: X = A[k-1 ..] (rcx), Y = A[k ..] (rcx + 8)
	mov r11, r8
	dec r11
.shlBlocks:
	cmp r11, 4
	jb .shlWords
	vmovdqu ymm0, [rcx]
	vmovdqu ymm1, [rcx + 8]
	vpsllq ymm0, ymm0, xmm4
	vpsrlq ymm1, ymm1, xmm5
	vpor ymm0, ymm0, ymm1
	vmovdqu [rdx], ymm0
	add rcx, 32
	add rdx, 32
	sub r11, 4
	jmp .shlBlocks
.shlWords:
	test r11, r11
	jz .shlLowest
	vmovq xmm0, [rcx]
	vmovq xmm1, [rcx + 8]
	vpsllq xmm0, xmm0, xmm4
	vpsrlq xmm1, xmm1, xmm5
	vpor xmm0, xmm0, xmm1
	vmovq [rdx], xmm0
	add rcx, 8
	add rdx, 8
	dec r11
	jmp .shlWords
.shlLowest:                     ; k = lenA: A[lenA-1] << s
	vmovq xmm0, [rcx]
	vpsllq xmm0, xmm0, xmm4
	vmovq [rdx], xmm0
	add rdx, 8

	ZERO_UP rdx, r10            ; ws zero words at the low end
	vzeroupper
	ret

;------------------------------------------------------------------------------
global Fullint64ShrWIN_avx2
Fullint64ShrWIN_avx2:
	movsxd r8, r8d              ; lenA
	movsxd r9, r9d              ; lenR
	mov eax, [rsp+28h]          ; shift
	mov r10, rax
	shr r10, 6                  ; ws
	and eax, 63                 ; s
	vmovq xmm4, rax
	mov r11d, 64
	sub r11, rax
	vmovq xmm5, r11             ; 64 - s
	vpxor ymm3, ymm3, ymm3

	mov r11, r8
	sub r11, r10                ; count = lenA - ws source words
	jle .allZero
	mov r10, r9
	sub r10, r11                ; lead = lenR - count (zeroed last)
	lea rax, [rdx + r10*8]      ; R[lead]: k = 0's place

	; from the low end: k = r11 - 4 .. r11 - 1 while k >= 1, both loads before the store
.shrBlocks:
	cmp r11, 5
	jb .shrWords
	vmovdqu ymm0, [rcx + r11*8 - 32]    ; A[k ..]
	vmovdqu ymm1, [rcx + r11*8 - 40]    ; A[k-1 ..]
	vpsrlq ymm0, ymm0, xmm4
	vpsllq ymm1, ymm1, xmm5
	vpor ymm0, ymm0, ymm1
	vmovdqu [rax + r11*8 - 32], ymm0
	sub r11, 4
	jmp .shrBlocks
.shrWords:                      ; k = r11 - 1 >= 1
	cmp r11, 2
	jb .shrTop
	vmovq xmm0, [rcx + r11*8 - 8]
	vmovq xmm1, [rcx + r11*8 - 16]
	vpsrlq xmm0, xmm0, xmm4
	vpsllq xmm1, xmm1, xmm5
	vpor xmm0, xmm0, xmm1
	vmovq [rax + r11*8 - 8], xmm0
	dec r11
	jmp .shrWords
.shrTop:                        ; k = 0: A[0] >> s
	vmovq xmm0, [rcx]
	vpsrlq xmm0, xmm0, xmm4
	vmovq [rax], xmm0

	ZERO_UP rdx, r10            ; lead zeros, after every read of A
	vzeroupper
	ret

.allZero:                       ; lenA <= ws: everything shifted out
	ZERO_UP rdx, r9
	vzeroupper
	ret
