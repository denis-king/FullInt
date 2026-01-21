; main.asm
default rel
section .data
    ; входная строка
    input db "191408831393027885698148216680369618943",0
    input_len equ 39          ; длина строки без нуля

    ; буфер для результата (например 128 qword = 1024 байт)
    output resq 3

    ; переменная для длины буфера
    output_len dq 3

section .text
global main
extern Fullint64FromDecStringWin

main:
    ; rcx = адрес строки
    lea rcx, [input]

    ; rdx = адрес буфера
    lea rdx, [output]

    ; r8 = длина строки
    mov r8, input_len

    ; r9 = адрес переменной длины буфера
    lea r9, [output_len]

    ; вызов твоей обёртки
    call Fullint64FromDecStringWin

    ret