
default rel
%include "abi.inc"
CODE_SECTION

;------------------------------------------------------------------------------------
ENTRY Fullint64FromDecStringWin
	SAVE rdi, rsi, rbx, r12, r13
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
Fullint64FromDecString_body:
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
	ARET rdi, rsi, rbx, r12, r13

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
