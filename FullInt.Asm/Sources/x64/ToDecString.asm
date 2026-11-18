
default rel
%include "abi.inc"
CODE_SECTION


ENTRY Fullint64ToDecStringWin
	SAVE rdi, rsi, rbx, r12
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d
	mov r8, STACKARG(5, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd rdx, edx
%endif
Fullint64ToDecString_body:
    xor rbx, rbx
    mov r11, 10000000000000000000 ;max ulong dec capacity
    mov r12, 0CCCCCCCCCCCCCCCDh ; ceil(2^67 / 10): x / 10 = (x * r12) >> 67 for any 64-bit x

.copyLoop:
    mov rax, [rdi + rbx * 8]
    mov [rsi + rbx * 8], rax
    inc rbx
    cmp rbx, rdx
    jne .copyLoop


    mov rdi, rdx; value len
    movsxd r10, dword [r8]; result len value (ref int: 4 bytes)
   

.mainLoop:
    ;todo restore value lenth 
    ; and len possible to be less for each iteration
    xor rdx, rdx
    mov r9, rdi
.div2longLoop:
	;exif if it's the end of dividend
	test r9, r9
	jz .div2longDone
	
	;read next word
	mov rax, [rsi]
	
	;rdx:rax / rsi
	div r11; max dec
    ;rax result, rdx remainder
    
    mov [rsi], rax
    add rsi, 8
	
	test rax, rax ; 
    jnz .storeDiv
   
    cmp rdi, r9
    jne .storeDiv
    dec rdi ; decrement the result len for the next iterations
.storeDiv:
	dec r9
    jmp .div2longLoop

.div2longDone:
	;restore addres to the normal start of the rezult buff
    mov rbx, rdi
    shl rbx, 3
    sub rsi, rbx
	
    mov rax, rdx
    mov rbx, 19
.charLoop:
    dec rbx
    dec r10
    js .done

    ;rax / 10 by the reciprocal (was div r12 with r12 = 10); r9 is free here
    mov r9, rax
    mul r12
    shr rdx, 3 ;q = rax / 10
    mov rax, rdx
    lea rdx, [rdx + rdx*4]
    add rdx, rdx ;q * 10
    sub r9, rdx ;the digit

    ;convert value 0 - 9 to char and stor to the rez buff
    add r9b, 0x30
    mov [rcx + r10], r9b
    
    test rax, rax
    jnz .charLoop
    
    ;compare the temp value with zero and break if zero
    cmp rdi, 1 ; compare by len (normilazed value)
    ja .continue ;len more then 1
    jb .done; len zero
    mov rax, [rsi]
    test rax, rax
    jz .done ;the temp value is zero
    
.continue:
    mov dl, 0x30

.storeZeroChars:
    test rbx, rbx
    jz .mainLoop
    js .mainLoop
    
    dec r10
    js .done
    
    mov [rcx + r10], dl
    dec rbx
    jmp .storeZeroChars
    
.done:
    mov [r8], r10d ;retur the start position of the result chars (ref int: 4 bytes)
	ARET rdi, rsi, rbx, r12

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
