
; =============================================================================
;  x86 (32-bit) port of x64/Compare.asm. cdecl, arguments on the stack.
;
;  int Fullint64CompareWIN(A, B, int lenA, int lenB)   -> 1, -1 or 0
;
;  Register map (x64 -> x86): rdi A -> esi, rsi B -> edi, rcx lenA -> ecx,
;  rdx lenB -> edx, rbx index -> ebx, rax -> eax. A limb is compared high dword first.
; =============================================================================

section .text

global Fullint64CompareWIN
Fullint64CompareWIN:
	push ebx
	push esi
	push edi
	mov esi, [esp+16]             ; A
	mov edi, [esp+20]             ; B
	mov ecx, [esp+24]             ; lenA
	mov edx, [esp+28]             ; lenB

.skipZerosA:
	mov eax, [esi]
	or eax, [esi + 4]
	jnz .skipZerosB
	cmp ecx, 1
	je .skipZerosB
	add esi, 8
	dec ecx
	jmp .skipZerosA

.skipZerosB:
	mov eax, [edi]
	or eax, [edi + 4]
	jnz .compare
	cmp edx, 1
	je .compare
	add edi, 8
	dec edx
	jmp .skipZerosB

.compare:
	cmp ecx, edx
	ja .greate
	jb .less

	xor ebx, ebx

.comparationLoop:
	mov eax, [esi + ebx*8 + 4]    ; high dword
	cmp eax, [edi + ebx*8 + 4]
	ja .greate
	jb .less
	mov eax, [esi + ebx*8]        ; low dword
	cmp eax, [edi + ebx*8]
	ja .greate
	jb .less

	inc ebx
	cmp ebx, edx
	jne .comparationLoop

	;we have equal A and B
	xor eax, eax
	jmp .done
.less:
	mov eax, -1
	jmp .done
.greate:
	mov eax, 1

.done:
	pop edi
	pop esi
	pop ebx
	ret
