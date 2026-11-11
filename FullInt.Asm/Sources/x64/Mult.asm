
default rel
section .text

global Fullint64MultWIN
Fullint64MultWIN:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push rbx
	push r12
	push r13
	push r14
	push r15
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d             ; lenA (int)
	movsxd r8, dword [rsp+60h]  ; lenB (int; 28h + 7 pushes)
	movsxd r9, dword [rsp+68h]  ; lenR (int; 30h + 7 pushes)

	call Fullint64Mult
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	pop rsi
	pop rdi
	ret

global Fullint64Mult
Fullint64Mult:
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    mov rbx, r10 ;copy the latest position
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8

	xor r13, r13
	xor r14, r14
	xor r15, r15
	
.outerIteration:
	lea r11, [rsi + r8*8 - 8]; set the latest B postion
	mov r9, r10; restore shifted A item with current A iter
.innerIteration:
	
	;skip if shifted A item is out of A range
	cmp r9, rdi
	jl .endInnerIteration
	
	cmp r9, rbx
	jg .endInnerIteration
	
	;mult ax to dx => dx:ax
	mov rax, [r9]
	mov rdx, [r11]
	mul rdx
	
	;add the result
	clc
	adc r13, rax ; addLo
	adc r14, rdx ; addMid
	adc r15, 0 ; addHi
	
	
.endInnerIteration:	
	add r9, 8; shift A index + B index
	
	;inner loop: B lenth
	cmp r11, rsi ;todo сравнить адрес с базовим если равны то выход
    je .innerDone
    sub r11, 8;move B index to the next position
	jmp .innerIteration

.innerDone:	
	mov [r12], r13 ;store the result
	mov r13, r14 ; mid to lo
	mov r14, r15 ; hi to mid
	xor r15, r15 ; zoro hi
	
	sub r10, 8; move A index to the next position
	;outer loop result lenth
	cmp r12, rcx ;todo сравнить адрес с базовим если равны то выход
    je .done
    sub r12, 8
	jmp .outerIteration
.done:
	ret