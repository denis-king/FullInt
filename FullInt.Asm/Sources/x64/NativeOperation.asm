; test.asm — минимальная Win64 функция для проверки NASM

default rel
%include "abi.inc"
section .text

; ------------------------------------------------------------
; uint64 Mul64(uint64 a, uint64 b, ref uint64 high)
; RCX = a, RDX = b, R8 = pointer to high
; Returns: RAX = low part, [R8] = high part
ENTRY Mul128
	WIN64_ARGS
    mov rax, rcx        ; a → RAX
    mul rdx             ; RAX * RDX → RDX:RAX
    mov [r8], rdx       ; high part → *high
    WRET

; ------------------------------------------------------------
; uint64 Div128(uint64 hi, uint64 lo, uint64 divisor, ref uint64 remainder)
; RCX = hi, RDX = lo, R8 = divisor, R9 = pointer to remainder
; Returns: RAX = quotient, [R9] = remainder
ENTRY Div128
	WIN64_ARGS
    mov r10, r8         ; divisor → R10
    mov rax, rdx        ; lo → RAX
    mov rdx, rcx        ; hi → RDX
    div r10             ; RDX:RAX / R10 → RAX = quotient, RDX = remainder
    mov [r9], rdx       ; remainder → *remainder
    WRET

; ------------------------------------------------------------
; uint64 Add64(uint64 a, uint64 b, ref byte carry)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
ENTRY Add64
	WIN64_ARGS
    add rcx, rdx		; rax = a + b + CF
    setc r9b			; r9 = new CF 
    mov byte [r8], r9b	; carryOut = r9
    WRET


; ------------------------------------------------------------
; uint64 Adc64(uint64 a, uint64 b, ref byte carry)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
ENTRY Adc64
	WIN64_ARGS
    movzx r9, byte [r8]	; r9 = carryIn
    shr r9b, 1			; CF = r9

    mov rax, rcx		; rax = a
    adc rax, rdx		; rax = a + b + CF
    setc r9b			; r9 = new CF
   
    mov byte [r8], r9b	; carryOut = r9
    WRET

; ------------------------------------------------------------
; uint64 Sub64(uint64 a, uint64 b, ref byte borrow)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
ENTRY Sub64
	WIN64_ARGS
    sub rax, rdx		; rax = a - b - CF
    setc r9b			; r9 = new CF
    mov byte [r8], r9b	; borrowOut = r9
    WRET

; ------------------------------------------------------------
; uint64 Sbb64(uint64 a, uint64 b, ref byte borrow)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
ENTRY Sbb64
	WIN64_ARGS
    movzx r9, byte [r8]	; r9 = borrowIn
    shr r9b, 1			; CF = r9

    mov rax, rcx		; rax = a
    sbb rax, rdx		; rax = a - b - CF
    setc r9b			; r9 = new CF
   
    mov byte [r8], r9b	; borrowOut = r9
    WRET
