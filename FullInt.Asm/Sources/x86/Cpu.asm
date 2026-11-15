; =============================================================================
;  x86 (32-bit): which kernel versions this CPU can run. cdecl.
;
;    int Fullint64CpuFeatures(void)
;      bit 4 (16): SSE2 (cpuid leaf 1, edx bit 26) - LogicSse2.asm
;
;  The x64 version (x64/Cpu.asm) reports bits 0..2 (BMI2, ADX, AVX2); the bit numbers do
;  not overlap, so the callers keep one set of flags for both.
;  cpuid itself is there on every CPU Windows runs on (Pentium and later).
; =============================================================================

section .text

global Fullint64CpuFeatures
Fullint64CpuFeatures:
	push ebx                    ; cpuid writes ebx (callee-saved in cdecl)
	xor eax, eax
	cpuid                       ; eax = the highest standard leaf
	xor ecx, ecx                ; the result
	cmp eax, 1
	jb .done
	mov eax, 1
	cpuid
	xor ecx, ecx
	bt edx, 26                  ; SSE2
	jnc .done
	or ecx, 16
.done:
	mov eax, ecx
	pop ebx
	ret
