
default rel
section .text align=64

;WINDOWS wrapper
global Fullint64AddWIN
Fullint64AddWIN:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push r12
	push r13
	push r14
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d             ; lenA (int)
	movsxd r8, dword [rsp+50h]  ; lenB (int; 28h + 5 pushes)
	movsxd r9, dword [rsp+58h]  ; lenR (int; 30h + 5 pushes)

	call Fullint64Add
	pop r14
	pop r13
	pop r12
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

; Code placement: keep the body at offset 1Ch mod 64 (where it was before the
; wrapper grew). The hot loop is placement-sensitive: other offsets measured
; 10-30% slower on Zen 3. Padding sits after the wrapper's ret, never executed.
	times (1Ch - ($ - $$)) & 63 int3
global Fullint64Add
Fullint64Add:
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    lea r11, [rsi + r8*8 - 8]     ; ptrB = baseB + lenB*8
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8

    ; Перенос
    xor eax, eax              ; AL = 0
   ; clc                       ; CF = 0

.mainLoop:
   
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
    adc r13, r14
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

    jmp .mainLoop

.done:
    ret