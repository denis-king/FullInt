; test.asm — минимальная Win64 функция для проверки NASM

default rel
section .text

global Normalize3Words
Normalize3Words:
    ; Вход:
    ; rcx = &D2
    ; rdx = &D1
    ; r8  = D0
    ; r9  = &U2
    ; [rsp+28h] = &U1
    ; [rsp+30h] = U0
	
	mov r15, [rcx] 
	mov r10, [r9]
	or  r15, r10 ; D2 | U2
	
    ; --- загрузка значений ---
    mov rax, [rcx]       ; D2
    mov rbx, [rdx]       ; D1
    mov r10, rcx         ; d2 address
    mov r11, rdx         ; d1 address
    mov r12, [rsp+28h]   ; &U1 address
    mov r13, [r12]       ; U1
    mov r14, [rsp+30h]   ; U0
	
    ; --- вычисление shiftLen ---
	xor rcx, rcx
	xor rdx, rdx
	
    bsr rdx, r15         ; индекс старшего бита D2
    mov rcx, 63
    sub rcx, rdx         ; shiftLen
	
    ; --- нормализация делителя ---
    shld rax, rbx, cl    ; новый D2
    shld rbx, r8, cl    ; новый D1
	
	mov r15, [r9] 		; U2
    ; --- нормализация делимого ---
    shld r15, r13, cl    ; новый U2
    shld r13, r14, cl    ; новый U1

	;-----------------------------
	cmp r15, rax
	jae temp
	
	mov rcx, rax ; d2 to cx
	mov rdx, r15 ; u2
	mov rax, r13 ; u1
	div rcx
	
	;exchange rax and r13
	xor rax, r13
	xor r13, rax ; resultMax to r13
	xor rax, r13 ; u1 to ax
	
	mov rdx, r15 ; u2
	inc rcx ; d2 + 1
	div rcx
	;resultMin in ax 
	jmp temp2
 temp:
	mov rcx, rax ; d2 to cx
	xor rdx, rdx ; 0
	mov rax, r15 ; u2
	div rcx
	mov r13, rax ; resultMax to r13
	
	xor rdx, rdx ; 0
	mov rax, r15 ; u2
	inc rcx ; d2 + 1
	div rcx
	;resultMin in ax 
	
 temp2:
	cmp rax, r13
	je exit
	
	mov rcx, r13 ; max to cx
	sub rcx, rax ; max - min
	inc rcx ; range
	xor rdx, rdx 
	mov rax, 0ffffffffffffffffh ;ulong max value
	div rcx ; 
	; step in ax
	mov rcx, rax ; step in cx
	mov rax, rbx 
	xor rdx, rdx
	div rcx ; range offset in ax
	
	sub r13, rax ; max - offset
	mov rax, r13 ; result in ax
  
  ; --- запись результатов обратно ---
  ;  mov [r10], rax       ; обновить D2
  ;  mov [r11], rbx       ; обновить D1
  ;  mov [r9],  r15        ; обновить U2
  ;  mov [r12], r13       ; обновить U1
	
	;xor rax, rax
	;mov al, cl
exit:    ret
