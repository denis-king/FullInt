
default rel
%include "abi.inc"
CODE_SECTION

; rax = the value of the 8 decimal digits at [rdi] (the first one the most significant), rdi += 8;
; rdx scratch, r12 = '0' in every byte, r13 = 00FF00FF00FF00FFh
%macro EIGHT_DIGITS 0
	mov rax, [rdi]
	sub rax, r12                  ; byte i: digit i
	mov rdx, rax
	shr rdx, 8
	imul rax, rax, 10
	add rax, rdx
	and rax, r13                  ; the even bytes: pairs (10 * d0 + d1)
	mov rdx, rax
	shr rdx, 16
	imul rax, rax, 100
	add rax, rdx
	mov rdx, 0000FFFF0000FFFFh
	and rax, rdx                  ; the even 16-bit lanes: fours
	mov rdx, rax
	shr rdx, 32
	imul rax, rax, 10000
	add rax, rdx
	mov eax, eax                  ; eight
	add rdi, 8
%endmacro

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
	mov r12, 3030303030303030h   ; '0' in every byte
	mov r13, 00FF00FF00FF00FFh
    
    mov rax, r9
    xor rdx, rdx
    mov rbx, 19
    div rbx
    test rdx, rdx
    jz .readLoop
    mov rbx, rdx

.readLoop:
	cmp rbx, 19
	jne .partial
	; a full chunk, 19 digits = 8 + 8 + 3: eight at once (SWAR - the bytes to digits, then pairs,
	; fours, eights by multiply and shift; the chunks do not wait for each other) against the
	; multiply per digit, whose chain cost ~5 cycles a digit
	EIGHT_DIGITS
	mov rdx, 100000000000         ; 10^11
	imul rax, rdx
	mov [rsi + r10 * 8], rax
	EIGHT_DIGITS
	imul rax, rax, 1000
	add [rsi + r10 * 8], rax
	movzx eax, byte [rdi]         ; the last three
	sub eax, '0'
	movzx edx, byte [rdi + 1]
	sub edx, '0'
	lea eax, [rax + rax * 4]
	lea eax, [rdx + rax * 2]
	movzx edx, byte [rdi + 2]
	sub edx, '0'
	lea eax, [rax + rax * 4]
	lea eax, [rdx + rax * 2]
	add rdi, 3
	add [rsi + r10 * 8], rax
	sub r9, 19
	jmp .stored
.partial:                         ; the first chunk when the length is not a multiple of 19: digit by digit
	xor eax, eax
.digit:
	movzx edx, byte [rdi]
	inc rdi
	sub edx, '0'
	lea rax, [rax + rax * 4]
	lea rax, [rdx + rax * 2]      ; x * 10 + d
	dec r9
	dec rbx
	jnz .digit
	mov [rsi + r10 * 8], rax
.stored:
	inc r10
	mov rbx, 19
	test r9, r9
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
