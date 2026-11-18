
default rel
%include "abi.inc"
CODE_SECTION


ENTRY Fullint64MultWIN
	SAVE rdi, rsi, rbx, r12, r13, r14, r15
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d
	movsxd r8, dword STACKARG(5, FI_NPUSH)
	movsxd r9, dword STACKARG(6, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd rdx, edx
	movsxd r8, r8d
	movsxd r9, r9d
%endif
Fullint64Mult_body:
	push rbp                      ; rbp = end of the column's A range (callee-saved in both ABIs)
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    mov rbx, r10 ;copy the latest position
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8
	lea r8, [rsi + r8*8 - 8]      ; r8 = the latest B position (lenB is not needed after this)

	xor r13, r13
	xor r14, r14
	xor r15, r15

	align 32                      ; the column loop on a line of its own (reached by jumps)
.outerIteration:
	; column bounds once per column, so the inner loop only walks real pairs:
	;   A from r9 = max(r10, baseA) up to rbp = min(lastA, r9 + (r11 - baseB))
	;   B from r11 = lastB - (r9 - r10) down
	mov r9, r10; restore shifted A item with current A iter
	cmp r9, rdi
	cmovb r9, rdi                 ; A item before A start -> start at A[0]
	lea r11, [r8 + r10]
	sub r11, r9                   ; B position paired with r9
	mov rbp, r11
	sub rbp, rsi                  ; bytes of B left below r11
	jb .innerDone                 ; no B words for this column (only when lenR > lenA + lenB)
	add rbp, r9
	cmp rbp, rbx
	cmova rbp, rbx                ; last A item of the column

	; inner loop unrolled by 2: an odd pair first, then pairs of pairs
	mov rax, rbp
	sub rax, r9                   ; (pairs - 1) * 8
	test al, 8
	jnz .innerPairs               ; even number of pairs
.innerIteration:
	;mult ax to dx => dx:ax
	mov rax, [r9]
	mov rdx, [r11]
	mul rdx

	;add the result
	add r13, rax ; addLo (same as clc + adc: CF starts at 0, sets CF for the chain below)
	adc r14, rdx ; addMid
	adc r15, 0 ; addHi

	add r9, 8; shift A index + B index
	sub r11, 8;move B index to the next position
	cmp r9, rbp
	ja .innerDone
.innerPairs:
	mov rax, [r9]
	mov rdx, [r11]
	mul rdx
	add r13, rax ; addLo
	adc r14, rdx ; addMid
	adc r15, 0 ; addHi

	mov rax, [r9 + 8]
	mov rdx, [r11 - 8]
	mul rdx
	add r13, rax ; addLo
	adc r14, rdx ; addMid
	adc r15, 0 ; addHi

	add r9, 16
	sub r11, 16
	cmp r9, rbp
	jb .innerPairs

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
	pop rbp
	ARET rdi, rsi, rbx, r12, r13, r14, r15

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
