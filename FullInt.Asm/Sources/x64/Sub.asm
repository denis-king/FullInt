
default rel
section .text

;WINDOWS wrapper
global Fullint64SubWIN
Fullint64SubWIN:
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

	call Fullint64Sub
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	pop rsi
	pop rdi
	ret

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW
global Fullint64Sub
Fullint64Sub:

.skipZerosA:
	mov rax, [rdi]
	test rax, rax
	jnz .skipZerosB
	cmp rdx, 1
	je .skipZerosB
	add rdi, 8
	dec rdx
	jmp .skipZerosA
	
.skipZerosB:
	mov rax, [rsi]
	test rax, rax
	jnz .compare
	cmp r8, 1
	je .compare
	add rsi, 8
	dec r8
	jmp .skipZerosB
	
	
.compare:	
	mov r15, 1
	cmp rdx, r8
	ja .init
	jb .swap
	
	xor rbx, rbx
.comparationLoop:
	mov rax, [rdi + rbx*8]
	cmp rax, [rsi + rbx*8]
	ja .init
	jb .swap
	
	inc rbx
	cmp rbx, rdx
	jne .comparationLoop
	
	;we have equal A and B
	xor rax, rax
	xor r15, r15

.zeroResultLoop:
	mov [rcx], rax
	add rcx, 8
	dec r9
	jnz .zeroResultLoop
	jmp .done
	
.swap:	
	xor rdi, rsi
	xor rsi, rdi
	xor rdi, rsi
	
	xor rdx, r8
	xor r8, rdx
	xor rdx, r8
	
	mov r15, -1
.init:
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    lea r11, [rsi + r8*8 - 8]     ; ptrB = baseB + lenB*8
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8
	
    xor rax, rax              ; AL = 0

.iteration:
   
    ; Загрузить слово A или 0
    xor r13, r13
    cmp r10, rdi              ; ptrA > baseA ? есть слова
    jl .skipA
    mov r13, [r10]  ; взять A[lenA-1] по текущему адресу
.skipA:

    ; Загрузить слово B или 0
    xor r14, r14
    cmp r11, rsi
    jl .skipB
    mov r14, [r11]
.skipB:
	
	  ; Восстановить CF из AL (AL бит0 > CF)
    shr al, 1
    ; Сложить A + B и добавить перенос ровно одним adc
    sbb r13, r14
	; Сохранить CF в AL, прежде чем портить флаги
    setc al
    ; Записать в R[lenR-1] по адресу (ptrR)
    mov [r12], r13 

    ; Если результат исчерпан — выход
    cmp r12, rcx ;todo сравнить адрес с базовим если равны то выход
    je .done
    sub r12, 8
    sub r10, 8
    sub r11, 8

    jmp .iteration

.done:
	mov rax, r15
    ret
