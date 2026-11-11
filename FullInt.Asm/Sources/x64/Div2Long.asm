
default rel
section .text

global Fullint64Div2longWin
Fullint64Div2longWin:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	mov rdi, rcx ;dividend buf
	mov rsi, rdx ;divisor long
	mov rcx, r8 ;result len r8 - r9 + 1 and atleast 1
	mov rdx, r9 ;remainder ref long
	movsxd r8, dword [rsp+38h] ;lenDividend (int; 28h + 2 pushes)


	call Fullint64Div2long
	pop rsi
	pop rdi
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