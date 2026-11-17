
default rel
%include "abi.inc"
section .text

; =============================================================================
;  ToDecString with the division by 10^19 through its reciprocal.
;  A new kernel next to ToDecString.asm (the baseline, kept as is): the same algorithm and
;  contract (Fullint64ToDecStringWin), only the "div r11" per word is replaced by the 2-by-1
;  division with a precomputed reciprocal (Moller-Granlund, as in DivRecip.asm). 10^19 has
;  its top bit set already: no normalization, and the reciprocal is a constant.
;    (q1:q0) = v * u1 + (u1 + 1 : u0)
;    r = u0 - q1 * d;  r > q0: q1 - 1, r + d;  r >= d (rare): q1 + 1, r - d
;  Plain x86-64 (mul / imul / add): any CPU; picked unless FULLINT_CPU=baseline.
;
;  void Fullint64ToDecStringWIN_recip(value, valueBuf, resultBuf, int lenValue, int* lenResult)
;
;  Registers: rdi original value, then its length; rsi temp value; rcx result chars;
;  r8 lenResult address; r10 char position; r11 = 10^19; r12 = 1/10 reciprocal;
;  r13 = v (1/10^19 reciprocal); r15 remainder (u1) in the word loop; r14 u0; rbx quotient word.
; =============================================================================
ENTRY Fullint64ToDecStringWIN_recip
	WIN64_ARGS
	push rdi
	push rsi
	push rbx
	push r12
	push r13
	push r14
	push r15
	mov rdi, rcx ;original value buff
	mov rsi, rdx ;temp value buff
	mov rcx, r8 ;result buf
	movsxd rdx, r9d ;value length (int)
	mov r8, [rsp+60h] ;result length addr (28h + 7 pushes)

	xor rbx, rbx
	mov r11, 10000000000000000000 ;max ulong dec capacity
	mov r12, 0CCCCCCCCCCCCCCCDh ; ceil(2^67 / 10): x / 10 = (x * r12) >> 67 for any 64-bit x
	mov r13, 0D83C94FB6D2AC34Ah ; floor((2^128 - 1) / 10^19) - 2^64

.copyLoop:
	mov rax, [rdi + rbx * 8]
	mov [rsi + rbx * 8], rax
	inc rbx
	cmp rbx, rdx
	jne .copyLoop

	mov rdi, rdx; value len
	movsxd r10, dword [r8]; result len value (ref int: 4 bytes)

.mainLoop:
	xor r15d, r15d
	mov r9, rdi
.div2longLoop:
	test r9, r9
	jz .div2longDone

	; (r15 : [rsi]) / 10^19 -> rbx, remainder r15
	mov r14, [rsi]
	mov rax, r13
	mul r15                     ; rdx:rax = v * u1
	add rax, r14                ; q0
	adc rdx, r15
	lea rbx, [rdx + 1]          ; q1
	mov rdx, rbx
	imul rdx, r11
	mov r15, r14
	sub r15, rdx                ; r = u0 - q1 * d
	cmp rax, r15                ; r > q0: one too many
	sbb rdx, rdx
	add rbx, rdx
	and rdx, r11
	add r15, rdx
	cmp r15, r11
	jb .stored
	sub r15, r11                ; rare: one too few
	inc rbx
.stored:
	mov [rsi], rbx
	add rsi, 8

	test rbx, rbx
	jnz .storeDiv
	cmp rdi, r9
	jne .storeDiv
	dec rdi ; decrement the result len for the next iterations
.storeDiv:
	dec r9
	jmp .div2longLoop

.div2longDone:
	;restore address to the normal start of the result buff
	mov rbx, rdi
	shl rbx, 3
	sub rsi, rbx

	; a chunk below the top one (the value is not zero yet) has all 19 digits: two chains at
	; once, the low 10 and the high 9 digits (r = H * 10^10 + L). The top chunk the same when
	; it has more than 10 digits, its leading zeros skipped after. Up to 10 digits and a result
	; buffer running out: the digit loop below.
	mov rax, r15
	cmp r10, 19
	jl .digits
	cmp rdi, 1
	ja .fullChunk
	jb .top
	cmp qword [rsi], 0
	jne .fullChunk
.top:
	mov rdx, 10000000000
	cmp r15, rdx
	jb .digits
.fullChunk:
	mov rax, r15
	shr rax, 10
	mov rdx, 06DF37F675EF6EBh   ; ceil(2^78 / 9765625): x / 10^10 = ((x >> 10) * M) >> 78
	mul rdx
	shr rdx, 14
	mov rax, 10000000000
	imul rax, rdx
	sub r15, rax                ; L = r mod 10^10
	mov r14, r15
	mov r15, rdx                ; H = r / 10^10 (< 10^9)
	lea rbx, [rcx + r10]        ; one past the chunk's lowest digit
	mov r9d, 0CCCCCCCDh         ; x / 10 = (x * r9) >> 35 for 32-bit x
%assign i 0
%rep 9
	mov rax, r14                ; L: x / 10 for any 64-bit x
	mul r12
	shr rdx, 3
	lea rax, [rdx + rdx*4]
	add rax, rax
	sub r14, rax
	add r14b, 0x30
	mov [rbx - 1 - i], r14b
	mov r14, rdx
	mov rax, r15                ; H
	imul rax, r9
	shr rax, 35
	lea rdx, [rax + rax*4]
	add rdx, rdx
	sub r15, rdx
	add r15b, 0x30
	mov [rbx - 11 - i], r15b
	mov r15, rax
%assign i i + 1
%endrep
	add r14b, 0x30              ; L's top digit
	mov [rbx - 10], r14b
	sub r10, 19
	cmp rdi, 1                  ; was it the top chunk?
	ja .mainLoop
	jb .trim
	cmp qword [rsi], 0
	jne .mainLoop
.trim:                          ; the top chunk: skip its leading zeros (H >= 1: at most 8)
	cmp byte [rcx + r10], 0x30
	jne .done
	inc r10
	jmp .trim

.digits:
	mov rbx, 19
.charLoop:
	dec rbx
	dec r10
	js .done

	;rax / 10 by the reciprocal; r9 is free here
	mov r9, rax
	mul r12
	shr rdx, 3 ;q = rax / 10
	mov rax, rdx
	lea rdx, [rdx + rdx*4]
	add rdx, rdx ;q * 10
	sub r9, rdx ;the digit

	add r9b, 0x30
	mov [rcx + r10], r9b

	test rax, rax
	jnz .charLoop

	;compare the temp value with zero and break if zero
	cmp rdi, 1 ; compare by len (normalized value)
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
	mov [r8], r10d ;return the start position of the result chars (ref int: 4 bytes)
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	pop rsi
	pop rdi
	WRET
