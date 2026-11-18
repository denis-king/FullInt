
default rel
%include "abi.inc"
CODE_SECTION


ENTRY Fullint64Div2longWin
	SAVE rdi, rsi
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	mov rdx, r9
	movsxd r8, dword STACKARG(5, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd r8, r8d
%endif
Fullint64Div2long_body:	
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
	ARET rdi, rsi

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
