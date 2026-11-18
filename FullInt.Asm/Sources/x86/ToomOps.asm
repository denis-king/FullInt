%include "abi86.inc"
CODE_SECTION

; =============================================================================
;  Exact division by a small constant for the Toom-Cook interpolation (x86 version of
;  Sources/x64/ToomOps.asm, the same contract). Baseline 32-bit instructions only.
;  Words are 64-bit and big-endian (word 0 the most significant), little-endian inside: the
;  low dword of word w at a + 8w, the high one at a + 8w + 4.
;
;  uint64 Fullint64DivExactWIN(a, q, int len, uint64 d)          (cdecl)
;    q = a / d for an a known to be a multiple of d; d = d'·2^t with d' odd, below 2^32 (and
;    t < 32); q may be a. Hensel division by dwords from the low end on x = a >> t (shrd with
;    the next dword up): inv = d'^-1 mod 2^32, per dword
;      q_i = (x_i - c)·inv,  c' = hi(q_i·d') + (x_i < c)
;    Returns the last c (edx:eax, edx = 0): 0 when a was a multiple.
; =============================================================================

; one dword: eax = x_i in, ebx = c in / out; %1 = where q_i goes
%macro STEP 1
	sub eax, ebx                ; x_i - c
	sbb ebx, ebx                ; -borrow
	imul eax, [esp]             ; q_i
	mov %1, eax
	mul dword [esp + 4]         ; edx = hi(q_i·d')
	sub edx, ebx                ; + borrow
	mov ebx, edx
%endmacro

ENTRY Fullint64DivExactWIN
	push ebx
	push esi
	push edi
	push ebp
	sub esp, 8                  ; [esp] inv, [esp + 4] d'
	; arguments: a [esp + 28], q [esp + 32], len [esp + 36], d [esp + 40] (low), [esp + 44]
	mov eax, [esp + 40]
	mov edx, [esp + 44]
	bsf ecx, eax                ; t (the low dword of d is not zero)
	shrd eax, edx, cl           ; d'
	mov [esp + 4], eax
	; inv = d'^-1 mod 2^32: (3d') xor 2 is right in 5 bits, each x·(2 - d'·x) doubles them
	lea ebx, [eax + eax*2]
	xor ebx, 2
%rep 3
	mov edx, eax
	imul edx, ebx               ; d'·x
	neg edx
	add edx, 2                  ; 2 - d'·x
	imul ebx, edx
%endrep
	mov [esp], ebx
	mov esi, [esp + 28]
	mov edi, [esp + 32]
	mov ebp, [esp + 36]
	xor ebx, ebx                ; c
	dec ebp                     ; words below the top one
	js .done
	lea esi, [esi + ebp*8]      ; the low word
	lea edi, [edi + ebp*8]
	jz .top
.loop:
	mov eax, [esi]
	mov edx, [esi + 4]
	shrd eax, edx, cl
	STEP [edi]
	mov eax, [esi + 4]
	mov edx, [esi - 8]          ; the low dword of the next word up
	shrd eax, edx, cl
	STEP [edi + 4]
	sub esi, 8
	sub edi, 8
	dec ebp
	jnz .loop
.top:
	mov eax, [esi]
	mov edx, [esi + 4]
	shrd eax, edx, cl
	STEP [edi]
	mov eax, [esi + 4]
	shr eax, cl                 ; nothing above the top word
	STEP [edi + 4]
.done:
	mov eax, ebx
	xor edx, edx
	add esp, 8
	pop ebp
	pop edi
	pop esi
	pop ebx
	ret

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
