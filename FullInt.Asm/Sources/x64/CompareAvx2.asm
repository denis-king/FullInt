
default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  Compare with AVX2 (Haswell, Zen and later; OS ymm support).
;  A new kernel next to Compare.asm (the baseline, kept as is); picked at start-up when
;  Fullint64CpuFeatures reports AVX2. Same contract:
;
;    int Fullint64CompareWIN_avx2(A, B, int lenA, int lenB)   -> -1, 0, 1  (|A| vs |B|)
;
;  Equal lengths (the engine's case: its quick check decides different lengths and top
;  words itself), from the top; the first word that differs decides (leading zero words do not
;  matter at equal lengths):
;    1-3 words: scalar;
;    4-8 words: the top four and the bottom four (overlapping below 8) at once, no loop (equal
;               numbers, C++: 4 words 10-20%, 8 words 25-30% faster than the loop);
;    9+:        four at a time - vpcmpeqq, vmovmskpd; the first lane that differs decides.
;  Different lengths: a tail jump into the baseline kernel, which skips leading zeros.
;
;  Registers (Win64 volatile only): rcx A, rdx B, r8 words left, r9 index, rax / r10 / r11
;  scratch; ymm0 / ymm1. vzeroupper on return.
; =============================================================================

EXTERNK Fullint64CompareWIN
ENTRY Fullint64CompareWIN_avx2
%ifndef FULLINT_WIN64
	cmp edx, ecx                ; System V: the lengths (arguments 3, 4) before the frame
	jne KREF(Fullint64CompareWIN)     ; different lengths: the baseline kernel (its own entry)
%endif
	WIN64_ARGS
	cmp r8d, r9d
	jne KREF(Fullint64CompareWIN)     ; different lengths: the baseline kernel

	movsxd r8, r8d
	cmp r8d, 8
	ja .long
	cmp r8d, 4
	jb .short
	; 4-8 words: the top four and the bottom four (they overlap below 8), no loop
	lea r10, [r8*8 - 32]        ; the bottom four's offset
	vmovdqu ymm0, [rcx]
	vpcmpeqq ymm0, ymm0, [rdx]
	vmovdqu ymm1, [rcx + r10]
	vpcmpeqq ymm1, ymm1, [rdx + r10]
	vmovmskpd eax, ymm0         ; bit i = lane i equal
	vmovmskpd r9d, ymm1
	vzeroupper
	xor eax, 15                 ; the differing lanes
	jnz .top4
	xor r9d, 15
	jnz .bottom4
	WRET                        ; eax = 0: equal
.bottom4:
	bsf r9d, r9d
	shr r10, 3
	add r9, r10
	jmp .decide4
.top4:
	bsf r9d, eax
.decide4:                       ; A[r9] != B[r9], ymm already clean
	mov r10, [rcx + r9*8]
	cmp r10, [rdx + r9*8]
	sbb eax, eax                ; -1 when A < B
	or eax, 1
	WRET
.short:                         ; 1-3 words
	xor r9d, r9d
.sw:
	mov rax, [rcx + r9*8]
	cmp rax, [rdx + r9*8]
	jne .sdecide
	inc r9
	cmp r9, r8
	jne .sw
	xor eax, eax
	WRET
.sdecide:
	sbb eax, eax                ; -1 when A < B
	or eax, 1
	WRET
.long:
	xor r9d, r9d                ; index from the top
	align 32                    ; the loop on a line of its own (abi.inc)
.blocks:
	cmp r8, 4
	jb .words
	vmovdqu ymm0, [rcx + r9*8]
	vpcmpeqq ymm0, ymm0, [rdx + r9*8]
	vmovmskpd eax, ymm0         ; bit i = lane i equal (lane 0 = the higher word)
	cmp eax, 15
	jne .lane
	add r9, 4
	sub r8, 4
	jmp .blocks
.lane:
	not eax
	bsf eax, eax                ; the first differing lane
	add r9, rax
	jmp .decide
.words:
	test r8, r8
	jz .equal
	mov rax, [rcx + r9*8]
	cmp rax, [rdx + r9*8]
	jne .decide
	inc r9
	dec r8
	jmp .words

.decide:                        ; A[r9] != B[r9]
	mov r10, [rcx + r9*8]
	mov r11, [rdx + r9*8]
	mov eax, 1
	mov r9d, -1
	cmp r10, r11
	cmovb eax, r9d
	vzeroupper
	WRET
.equal:
	xor eax, eax
	vzeroupper
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
