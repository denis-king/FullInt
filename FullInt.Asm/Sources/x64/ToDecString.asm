
default rel
section .text

global Fullint64ToDecStringWin
Fullint64ToDecStringWin:
	mov rdi, rcx ;originar value buff
	mov rsi, rdx ;temp value buff
	mov rcx, r8 ;result buf
	mov rdx, r9 ;value lenth
	mov r8, [rsp+28h] ;result lenth addr
	
	
	call Fullint64ToDecString
	ret

global Fullint64ToDecString
Fullint64ToDecString:
    xor rbx, rbx
    mov r11, 10000000000000000000 ;max ulong dec capacity
    mov r12, 10
    
.copyLoop:
    mov rax, [rdi + rbx * 8]
    mov [rsi + rbx * 8], rax
    inc rbx
    cmp rbx, rdx
    jne .copyLoop


    mov rdi, rdx; value len
    mov r10, [r8]; result len value
   

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
    xor rdx, rdx
    dec rbx
    dec r10
    js .done
    
    div r12; 10
     
    ;convert value 0 - 0 to char and stor to the rez buff
    add dl, 0x30
    mov [rcx + r10], dl
    
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
    mov [r8], r10 ;retur the start position of the result chars
	ret