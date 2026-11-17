
default rel
%include "abi.inc"
section .text

; the entry (abi.inc): one for Windows and System V, the arguments to the body's registers

ENTRY Fullint64CompareWIN
	SAVE rdi, rsi, rbx
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	movsxd rcx, r8d
	movsxd rdx, r9d
%else
	xchg rcx, rdx
	movsxd rcx, ecx
	movsxd rdx, edx
%endif
Fullint64Compare_body:

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
	
	align 32                      ; the hot loop on a line of its own (abi.inc)
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
    ARET rdi, rsi, rbx