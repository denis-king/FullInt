
default rel
section .text

global Fullint64Div2longWin
Fullint64Div2longWin:
	mov rdi, rcx ;dividend buf
	mov rsi, rdx ;divisor long
	mov rcx, r8 ;result len r8 - r9 + 1 and atleast 1
	mov rdx, r9 ;remainder ref long
	mov r8, [rsp+28h] ;lenDividend
	
	
	call Fullint64Div2long
	ret

global Fullint64Div2long
Fullint64Div2long:	
	;mov r10, rcx ; result address
	mov r11, rdx ; remainder address
    xor rdx, rdx

.mainLoop:

	;exif if it's the end of dividend
	test r8, r8
	jz .done
	
	;read next word
	mov rax, [rdi]
	add rdi, 8
	dec r8
	
	;rdx:rax / rsi
	div rsi
	;rax result, rdx remainder
	
	;store result
	mov [rcx], rax
	add rcx, 8

	
    jmp .mainLoop
	
.done:
    mov [r11], rdx
	ret