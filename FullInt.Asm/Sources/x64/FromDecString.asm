
default rel
section .text

global Fullint64FromDecStringWin
Fullint64FromDecStringWin:
	
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push rbx
	push r12
	push r13
	mov rdi, rcx ;originar value buff
	mov rsi, rdx ;temp value buff
	mov rcx, r8 ;result buf
	movsxd rdx, r9d ;value lenth (int)
	mov r8, [rsp+50h] ;result lenth addr (28h + 5 pushes)

	call Fullint64FromDecString
	pop r13
	pop r12
	pop rbx
	pop rsi
	pop rdi
	ret
;------------------------------------------------------------------------------------
global Fullint64FromDecString
Fullint64FromDecString:
    mov r9, rdx ;string len
    xor r10, r10
    mov r11, 10000000000000000000 ; 10^19
    mov r12, 10
    
    mov rax, r9
    xor rdx, rdx
    mov rbx, 19
    div rbx
    test rdx, rdx
    jz .readLoop
    mov rbx, rdx

.readLoop:
    xor rax, rax
.parseBloch:
    dec r9
    js .parseBlockDone
    xor rdx, rdx
    mul r12
    mov dl, [rdi]
    inc rdi
    sub dl, 0x30
    add rax, rdx
    dec rbx
    jnz .parseBloch
.parseBlockDone:  

    ;mov rax, [rdi + rbx * 8]
    mov [rsi + r10 * 8], rax
    inc r10

    mov rbx, 19
    cmp r9, 0
    jg .readLoop
.readDone:    
    mov rdi, r10; value len
    movsxd r10, dword [r8]; result len value (ref int: 4 bytes)
    
    ;srore rsi address
    mov r13, rsi
 
.mainLoop:
    ;todo restore value lenth 
    ; and len possible to be less for each iteration
    xor rdx, rdx
    mov r9, rdi
    
    ;restore rsi address
    mov rsi, r13
    
    xor rax, rax
    ;mov rax, [rsi]
   ; add rsi, 8
   ; dec r9
.div2longLoop:
	;exif if it's the end of dividend
    ;dec r9
	test r9, r9
	jz .div2longDone
	
   
    
	;read next word
	mov rbx, [rsi]
	
    xor rdx, rdx
	 ;rdx:rax / rcx
	mul r11; max dec
    add rax, rbx
    adc rdx, 0
   
    mov [rsi], rdx
    add rsi, 8
    
    ;decrease the buffer size
    test rdx, rdx
    jnz .div2longDec
    cmp r9, rdi
    jne .div2longDec
    dec rdi
    add r13, 8
.div2longDec:
	dec r9
    jmp .div2longLoop

.div2longDone:
    dec r10
    js .bufferOut
    
    
    mov [rcx + r10*8], rax
    
    ;exit if the buffer is empty
    cmp rdi, 1 ; compare by len, as in ToDecString (was test: it checked only the low bit)
    jb .done
    ja .mainLoop
    mov rax, [r13]
    test rax, rax
    jz .done
    
    jmp .mainLoop
    
    test r10, r10
.bufferOut:    
    xor r10, r10
.done:
    
    mov [r8], r10d ;retur the start position of the result chars (ref int: 4 bytes)
	ret