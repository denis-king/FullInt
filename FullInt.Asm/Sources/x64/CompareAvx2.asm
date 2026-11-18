
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
;  words itself): word by word from the top, four at a time - vpcmpeqq, vmovmskpd; the first
;  lane that differs decides. Leading zero words do not matter at equal lengths.
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
