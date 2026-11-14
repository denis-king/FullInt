
default rel
section .text

global Fullint64ToDecStringWin
Fullint64ToDecStringWin:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push rbx
	push r12
	mov rdi, rcx ;originar value buff
	mov rsi, rdx ;temp value buff
	mov rcx, r8 ;result buf
	movsxd rdx, r9d ;value lenth (int)
	mov r8, [rsp+48h] ;result lenth addr (28h + 4 pushes)


	call Fullint64ToDecString
	pop r12
	pop rbx
	pop rsi
	pop rdi
	ret

global Fullint64ToDecString
Fullint64ToDecString:
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
	ret