default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  ToDecString by Knuth's fraction method (TAOCP vol. 2, 4.4): the digits of a fraction come
;  out at the top when it is multiplied by the radix. The caller turns x (n words) into
;  f = x / 10^(19m) as an L-word fraction rounded up (one product by a precomputed reciprocal,
;  10^(19m) > x, L = m + 1); here m passes of f = f * 10^19, each one's integer part the next 19
;  digits from the top. A pass is a one-word multiply: the products do not wait for each other,
;  only the carry chains them (add / adc) - against a division by 10^19 per word, whose
;  remainder chains every step.
;
;  f stays an upper bound within the error the digits allow: after a pass its lowest word goes
;  and the new lowest is raised by one. After k passes the error is below (k + 2) * B^(k - L),
;  far below the gap 10^(-19(m - k)) between the possible fractions (B = 2^64 > 10^19), so
;  every integer part is exact.
;
;    void Fullint64ToDecFracWIN(uint64_t* f, int64 L, int64 m, uint8_t* out)
;      f: L words, index 0 the most significant (overwritten); out: 19 * m ASCII digits, the
;      top chunk's leading zeros included (the caller trims them).
;
;  Registers: rsi f, r10 active words, rdi out, r13 chunks left, r11 = 10^19, r12 = 1/10
;  reciprocal; the pass: r9 index, rcx carry; the digits: r14 / r15 the two halves, rbx.
; =============================================================================

ENTRY Fullint64ToDecFracWIN
	WIN64_ARGS
	push rbx
	push rsi
	push rdi
	push r12
	push r13
	push r14
	push r15
	mov rsi, rcx                  ; f
	mov r10, rdx                  ; active words
	mov rdi, r9                   ; out
	mov r13, r8                   ; chunks
	mov r11, 10000000000000000000
	mov r12, 0CCCCCCCCCCCCCCCDh   ; ceil(2^67 / 10): x / 10 = (x * r12) >> 67 for any 64-bit x

.pass:
	xor ecx, ecx                  ; the carry
	mov r9, r10
	align 16
.word:                            ; f = f * 10^19 from the lowest word up
	mov rax, [rsi + r9*8 - 8]
	mul r11
	add rax, rcx
	adc rdx, 0
	mov [rsi + r9*8 - 8], rax
	mov rcx, rdx
	dec r9
	jnz .word
	; rcx: the next 19 digits (< 10^19). f loses its lowest word, the new lowest + 1.
	cmp r10, 1
	je .digits
	dec r10
	mov r9, r10
.raise:
	add qword [rsi + r9*8 - 8], 1
	jnc .digits
	dec r9
	jnz .raise

.digits:                          ; rcx = H * 10^10 + L: two chains of 9 and 10 digits
	mov r15, rcx
	mov rax, rcx
	shr rax, 10
	mov rdx, 06DF37F675EF6EBh     ; ceil(2^78 / 9765625): x / 10^10 = ((x >> 10) * M) >> 78
	mul rdx
	shr rdx, 14
	mov rax, 10000000000
	imul rax, rdx
	sub r15, rax                  ; L = r mod 10^10
	mov r14, r15
	mov r15, rdx                  ; H = r / 10^10 (< 10^9)
	lea rbx, [rdi + 19]           ; one past the chunk's lowest digit
	mov r9d, 0CCCCCCCDh           ; x / 10 = (x * r9) >> 35 for 32-bit x
%assign i 0
%rep 9
	mov rax, r14                  ; L
	mul r12
	shr rdx, 3
	lea rax, [rdx + rdx*4]
	add rax, rax
	sub r14, rax
	add r14b, 0x30
	mov [rbx - 1 - i], r14b
	mov r14, rdx
	mov rax, r15                  ; H
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
	add r14b, 0x30                ; L's top digit
	mov [rbx - 10], r14b
	add rdi, 19
	dec r13
	jnz .pass

	pop r15
	pop r14
	pop r13
	pop r12
	pop rdi
	pop rsi
	pop rbx
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
