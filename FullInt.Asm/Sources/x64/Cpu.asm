
default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  CPU features for picking kernel versions at start-up.
;
;  int Fullint64CpuFeatures(void)   (both ABIs: no arguments, result in eax)
;    bit 0  BMI2   (mulx, shlx, shrx)          cpuid(7,0).ebx bit 8
;    bit 1  ADX    (adcx, adox)                cpuid(7,0).ebx bit 19
;    bit 2  AVX2   with OS support for ymm     cpuid(7,0).ebx bit 5, cpuid(1).ecx OSXSAVE (27),
;                                              xgetbv(0) xmm + ymm state (bits 1, 2)
;
;  The baseline kernels (the rest of this folder) need none of these: x86-64 as on a Core 2.
;  rbx is used by cpuid and saved here (non-volatile in both ABIs).
; =============================================================================

global Fullint64CpuFeatures
Fullint64CpuFeatures:
	push rbx
	xor r8d, r8d                ; result

	xor eax, eax
	cpuid
	cmp eax, 7
	jb .done                    ; no leaf 7

	mov eax, 1
	xor ecx, ecx
	cpuid
	mov r9d, ecx                ; leaf 1 ecx: OSXSAVE, AVX

	mov eax, 7
	xor ecx, ecx
	cpuid                       ; leaf 7 ebx: BMI2, ADX, AVX2

	bt ebx, 8
	jnc .noBmi2
	or r8d, 1
.noBmi2:
	bt ebx, 19
	jnc .noAdx
	or r8d, 2
.noAdx:
	bt ebx, 5
	jnc .done                   ; no AVX2
	bt r9d, 27
	jnc .done                   ; the OS does not save the ymm state: no xgetbv either
	xor ecx, ecx
	xgetbv                      ; edx:eax = XCR0
	and eax, 6
	cmp eax, 6
	jne .done
	or r8d, 4

.done:
	mov eax, r8d
	pop rbx
	ret

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
