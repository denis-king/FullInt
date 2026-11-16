
default rel
section .text

;WINDOWS wrapper
global Fullint64SubWIN
Fullint64SubWIN:
	; Win64 non-volatile registers used by the body (saved once per call)
	push rdi
	push rsi
	push rbx
	push r12
	push r13
	push r14
	push r15
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	movsxd rdx, r9d             ; lenA (int)
	movsxd r8, dword [rsp+60h]  ; lenB (int; 28h + 7 pushes)
	movsxd r9, dword [rsp+68h]  ; lenR (int; 30h + 7 pushes)

	call Fullint64Sub
	pop r15
	pop r14
	pop r13
	pop r12
	pop rbx
	pop rsi
	pop rdi
	ret

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW
global Fullint64Sub
Fullint64Sub:

.skipZerosA:
	mov rax, [rdi]
	test rax, rax
	jnz .skipZerosB
	cmp rdx, 1
	je .skipZerosB
	add rdi, 8
	dec rdx
	jmp .skipZerosA
	
.skipZerosB:
	mov rax, [rsi]
	test rax, rax
	jnz .compare
	cmp r8, 1
	je .compare
	add rsi, 8
	dec r8
	jmp .skipZerosB
	
	
.compare:	
	mov r15, 1
	cmp rdx, r8
	ja .init
	jb .swap
	
	xor rbx, rbx
.comparationLoop:
	mov rax, [rdi + rbx*8]
	cmp rax, [rsi + rbx*8]
	ja .init
	jb .swap
	
	inc rbx
	cmp rbx, rdx
	jne .comparationLoop
	
	;we have equal A and B
	xor rax, rax
	xor r15, r15

.zeroResultLoop:
	mov [rcx], rax
	add rcx, 8
	dec r9
	jnz .zeroResultLoop
	jmp .done
	
.swap:	
	xor rdi, rsi
	xor rsi, rdi
	xor rdi, rsi
	
	xor rdx, r8
	xor r8, rdx
	xor rdx, r8
	
	mov r15, -1
.init:
	; here |A| >= |B| and lenA >= lenB (leading zeros skipped). Lengths are resolved once,
	; so the loops have no per-word range checks. R is walked from its lowest word up:
	;   1) k1 = min(lenB, lenR) words of A - B - borrow   (borrow stays in CF: dec/lea/mov keep it)
	;   2) k2 = min(lenA, lenR) - k1 words of A - borrow
	;   3) k3 = lenR - min(lenA, lenR) words of zeros     (no borrow is left when |A| >= |B|)
	lea r10, [rdi + rdx*8]        ; end of A (past its lowest word)
	lea r11, [rsi + r8*8]         ; end of B
	lea r12, [rcx + r9*8]         ; end of R
	cmp r8, r9
	cmova r8, r9                  ; k1 = min(lenB, lenR)
	cmp rdx, r9
	cmova rdx, r9                 ; min(lenA, lenR)
	sub r9, rdx                   ; k3
	sub rdx, r8                   ; k2
	mov rax, rdx
	neg rax                       ; rax = -k2, moves the bases to run 2 without touching flags
	lea r13, [r8*8]
	sub r10, r13                  ; bases = top word of run 1, index r8 runs k1-1 .. 0 (low word first)
	sub r11, r13
	sub r12, r13
	mov rcx, r8
	shr rcx, 2                    ; groups of four
	mov r14, r8
	and r14, 3                    ; single words first
	dec r8
	test r14, r14                 ; (flags tested before the chain: clc keeps ZF)
	clc
	jz .subGroups
.subSingle:                      ; 1) A - B, the low k1 mod 4 words
	mov r13, [r10 + r8*8]
	sbb r13, [r11 + r8*8]
	mov [r12 + r8*8], r13
	dec r8
	dec r14                       ; (dec keeps CF)
	jnz .subSingle
.subGroups:
	jrcxz .subRun1Done
.subGroup:                       ; four words a pass: 0.8x of one a pass at 64-256 words (Zen 3)
	mov r13, [r10 + r8*8]
	sbb r13, [r11 + r8*8]
	mov [r12 + r8*8], r13
	mov r13, [r10 + r8*8 - 8]
	sbb r13, [r11 + r8*8 - 8]
	mov [r12 + r8*8 - 8], r13
	mov r13, [r10 + r8*8 - 16]
	sbb r13, [r11 + r8*8 - 16]
	mov [r12 + r8*8 - 16], r13
	mov r13, [r10 + r8*8 - 24]
	sbb r13, [r11 + r8*8 - 24]
	mov [r12 + r8*8 - 24], r13
	lea r8, [r8 - 4]
	dec rcx
	jnz .subGroup
.subRun1Done:
	mov rcx, rdx                  ; rcx = k2 (tested by jrcxz, which keeps CF)
	jrcxz .tail
	lea r10, [r10 + rax*8]        ; bases = top word of run 2
	lea r12, [r12 + rax*8]
	lea rcx, [rcx - 1]
.subBorrow:                       ; 2) A - borrow
	mov r13, [r10 + rcx*8]
	sbb r13, 0
	mov [r12 + rcx*8], r13
	dec rcx
	jns .subBorrow

.tail:
	test r9, r9
	jz .done
	xor eax, eax
.zeros:                           ; 3) zeros
	sub r12, 8
	mov [r12], rax
	dec r9
	jnz .zeros

.done:
	mov rax, r15
    ret
