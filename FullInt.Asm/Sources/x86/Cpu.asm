; =============================================================================
;  x86 (32-bit): which kernel versions this CPU can run. cdecl.
;
;    int Fullint64CpuFeatures(void)
;      bit 0: BMI2, bit 1: ADX (cpuid leaf 7, ebx bits 8 / 19) - MultAdx.asm (both)
;      bit 4 (16): SSE2 (cpuid leaf 1, edx bit 26) - LogicSse2.asm
;
;  The x64 version (x64/Cpu.asm) reports bits 0..2 (BMI2, ADX, AVX2): the same bit for the
;  same feature, so the callers keep one set of flags for both.
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
	push eax                    ; the highest leaf
	mov eax, 1
	cpuid
	push 0                      ; the result, on the stack (cpuid writes ecx)
	bt edx, 26                  ; SSE2
	jnc .noSse2
	or dword [esp], 16
.noSse2:
	cmp dword [esp+4], 7
	jb .leaf7Done
	mov eax, 7
	xor ecx, ecx
	cpuid
	bt ebx, 8                   ; BMI2
	jnc .noBmi2
	or dword [esp], 1
.noBmi2:
	bt ebx, 19                  ; ADX
	jnc .leaf7Done
	or dword [esp], 2
.leaf7Done:
	pop ecx
	add esp, 4
.done:
	mov eax, ecx
	pop ebx
	ret
