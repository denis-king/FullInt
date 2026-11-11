
default rel
section .text

;WINDOWS wrapper
global Fullint64CompareWIN
Fullint64CompareWIN:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push rbx
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	movsxd rcx, r8d             ; lenA (int)
	movsxd rdx, r9d             ; lenB (int)
	;mov r8, [rsp+28h]
	;mov r9, [rsp+30h]

	call Fullint64Compare
	pop rbx
	pop rsi
	pop rdi
	ret

global Fullint64Compare
Fullint64Compare:

.skipZerosA:
	mov rax, [rdi]
	test rax, rax
	jnz .skipZerosB
	cmp rcx, 1
	je .skipZerosB
	add rdi, 8
	dec rcx
	jmp .skipZerosA
	
.skipZerosB:
	mov rax, [rsi]
	test rax, rax
	jnz .compare
	cmp rdx, 1
	je .compare
	add rsi, 8
	dec rdx
	jmp .skipZerosB
	
.compare:	
	cmp rcx, rdx
	ja .greate
	jb .less
	
	xor rbx, rbx
	
.comparationLoop:
	mov rax, [rdi + rbx*8]
	cmp rax, [rsi + rbx*8]
	ja .greate
	jb .less
	
	inc rbx
	cmp rbx, rdx
	jne .comparationLoop
	
	;we have equal A and B
	xor rax, rax
	jmp .done
.less:	
	mov rax, -1
	jmp .done
.greate:
    mov rax, 1

.done:
    ret
