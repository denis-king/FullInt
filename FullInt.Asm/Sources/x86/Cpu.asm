; =============================================================================
;  x86 (32-bit): which kernel versions this CPU can run. cdecl.
;
;    int Fullint64CpuFeatures(void)
;      bit 0: BMI2, bit 1: ADX (cpuid leaf 7, ebx bits 8 / 19) - MultAdx.asm (both)
;      bit 2: AVX2 with OS support for ymm: leaf 7 ebx bit 5, leaf 1 ecx OSXSAVE (27),
;             xgetbv(0) xmm + ymm state (bits 1, 2) - NttAvx2.asm
;      bit 4 (16): SSE2 (cpuid leaf 1, edx bit 26) - LogicSse2.asm
;
;  The x64 version (x64/Cpu.asm) reports bits 0..2 (BMI2, ADX, AVX2): the same bit for the
;  same feature, so the callers keep one set of flags for both.
;  cpuid itself is there on every CPU Windows runs on (Pentium and later).
; =============================================================================

%include "abi86.inc"
section .text

ENTRY Fullint64CpuFeatures
	push ebx                    ; cpuid writes ebx (callee-saved in cdecl)
	xor eax, eax
	cpuid                       ; eax = the highest standard leaf
	xor ecx, ecx                ; the result
	cmp eax, 1
	jb .done
	push eax                    ; the highest leaf
	mov eax, 1
	cpuid
	push ecx                    ; leaf 1 ecx (OSXSAVE)
	push 0                      ; the result, on the stack (cpuid writes ecx)
	bt edx, 26                  ; SSE2
	jnc .noSse2
	or dword [esp], 16
.noSse2:
	cmp dword [esp+8], 7
	jb .leaf7Done
	mov eax, 7
	xor ecx, ecx
	cpuid
	bt ebx, 8                   ; BMI2
	jnc .noBmi2
	or dword [esp], 1
.noBmi2:
	bt ebx, 19                  ; ADX
	jnc .noAdx
	or dword [esp], 2
.noAdx:
	bt ebx, 5                   ; AVX2
	jnc .leaf7Done
	bt dword [esp+4], 27
	jnc .leaf7Done              ; the OS does not save the ymm state: no xgetbv either
	xor ecx, ecx
	xgetbv                      ; edx:eax = XCR0
	and eax, 6
	cmp eax, 6                  ; xmm and ymm state
	jne .leaf7Done
	or dword [esp], 4
.leaf7Done:
	pop ecx
	add esp, 8
.done:
	mov eax, ecx
	pop ebx
	ret
