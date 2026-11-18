
default rel
%include "abi.inc"
CODE_SECTION


ENTRY Fullint64DivWin
	SAVE rdi, rsi, rbx, r12, r13, r14
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	mov rdx, r9
	movsxd r8, dword STACKARG(5, FI_NPUSH)
	movsxd r9, dword STACKARG(6, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd r8, r8d
	movsxd r9, r9d
%endif
Fullint64Div_body:	
	mov r10, rcx ; result address
	mov r11, rdx ; remainder address
   
   ;todo possible we shold clenup the result and remainder buffers
   ;todo possible we shold normileze the divisor buf to skip hi zeros, but it will cause increase the result lenth
   ;so we expect the normilazed divisor!
   
	cmp r8, r9
	jae .copyPardDiv
	

	
;copy divident to remainder
	mov rax, r9
	sub rax, r8
	shl rax, 3 ; mul 8                                                                                                
	add r11, rax ; now we skipe hi words, the buffer lenth from position is divident + 2
	xor rcx, rcx ; loop index (rcx still held the result address here)
.copyDividentToRemainderLoop:
	mov rax, [rdi + rcx*8]
	mov [r11+rcx*8+16], rax
	inc rcx
	cmp rcx, r8
	jb .copyDividentToRemainderLoop
	
	;zero the result if its not and exit
	xor rax, rax
	mov [r10], rax

	jmp .done

;copy dividend from start and with divisor lenth to thi remainder as a part dividend
.copyPardDiv:	
	xor rcx, rcx
.copyLoop: 
	mov rax, [rdi]
	mov [r11+rcx*8+16], rax
	add rdi, 8
	dec r8
	inc rcx
	cmp rcx, r9
	jb .copyLoop
	

.mainLoop:
;comparation partDivident and divisor
	mov rax, [r11+8]
	test rax, rax
	jnz .findCandidate
	
	xor rcx, rcx
.comparationLoop:
	mov rax, [r11 + rcx*8 + 16]
	cmp rax, [rsi + rcx*8]
	ja .findCandidate
	jb .cpmpareLess
	
	inc rcx
	cmp rcx, r9
	jne .comparationLoop
	jmp .cpmpareEqual
.cpmpareLess:	
	;we have the less partDividend then divisor
	;so the result is 0
	xor rax, rax
	mov [r10], rax
	add r10, 8
	jmp .readNexWord
	
	
.cpmpareEqual:	
	cmp rcx, r9
	jb .comparationLoop
	;we have the equal partDividend and divisor
	;so the result is 1
	mov rax, 1
	mov [r10], rax
	add r10, 8
	
	;clear the reminder buf
	xor rcx, rcx
.clearLoop:
	xor rax, rax
	mov [r11 + rcx*8 + 16], rax
	inc rcx
	cmp rcx, r9
	jb .clearLoop
	
	jmp .readNexWord

.findCandidate:

	xor rcx, rcx
	;todo check lenth of the D and U buffers
	mov rax, [r11 + 8] ; Uhi
	test rax, rax
	jnz .getU2
	mov rax, [r11 + 16] ; Ulo
	xor rdx, rdx ;Uhi
	xor ecx, ecx ; normalize by Dhi only (Ulo may be > Dhi; shld moves its top bits into Uhi)
	jmp .getD1
.getU2:
	mov rdx, rax ; Uhi
	mov rax, [r11 + 16] ;Ulo
	mov rcx, rdx
.getD1:
	mov rbx, [rsi] ; Dhi
	
	cmp r9, 1
	ja .calcBit
	;if len divided 1, just div and store
	div rbx
	;store result
	mov [r10], rax
	add r10, 8
	;store remainder into the last window word; .readNexWord stops at the end
	;of the dividend or shifts it up and loads the next word
	mov qword [r11 + 8], 0 ; Uhi consumed by div
	mov [r11 + 16], rdx
	jmp .readNexWord
	 
.calcBit:	
	;calc bit position
	or rcx, rbx ; Dhi or Uhi
	bsr rcx, rcx ; calc bit position
	neg rcx
	add rcx, 63 ; get position from the left side
	
    ; --- нормализация dividend ---
    shld rdx, rax, cl    ; новый Uhi
	xor r12, r12
	cmp r9, 1
	je .shistUlo
	mov r12, [r11 + 24]
.shistUlo:	
	shld rax, r12, cl    ; новый Ulo
    mov r12, rdx
	mov r13, rax
	
	;todo move the norbalization before loop
    ; --- нормализация divisor ---
	xor r14, r14
	xor rax, rax
	cmp r9, 2
	jb .shiftDhi
	mov r14, [rsi + 8]
	je .shiftDhi
	mov rax, [rsi + 16]
	
.shiftDhi:	
    shld rbx, r14, cl    ; новый Dhi
    shld r14, rax, cl    ; новый Dlo

;step1
	mov rdx, r12 ; u2
	mov rax, r13 ; u1
	cmp rdx, rbx
	jae .maxOverflow ; u2 >= Dhi: u2:u1 / Dhi does not fit 64 bits (#DE)
	div rbx

	;exchange rax and r13
	xor rax, r13
	xor r13, rax ; resultMax to r13
	xor rax, r13 ; u1 to ax
.minStep:
	mov rdx, r12 ; u2

	cmp rbx, -1
	je .step2 ; Dhi + 1 = 2^64: min = u2:u1 / 2^64 = u2, already in r12
	inc rbx ; d2 + 1
	div rbx

	mov r12, rax ; min result


;step2
.step2:
	cmp r12, r13
	je .checkResult

	mov rcx, r13 ; max to cx
	sub rcx, r12 ; max - min
	inc rcx ; range
	jz .checkResult ; range = 2^64 (max = 2^64-1, min = 0): keep max, corrections below fix it
	xor rdx, rdx
	mov rax, 0ffffffffffffffffh ;ulong max value
	div rcx ; 
	; step in ax
	mov rcx, rax ; step in cx
	
	xor rdx, rdx
	mov rax, r14 ; Ulo
	
	div rcx ; range offset in ax
	
	sub r13, rax ; max - offset
	jc .clampMin ; offset > max
	cmp r13, r12
	jae .clampDone
.clampMin:
	mov r13, r12 ; never below min: the true digit is in [min, max]
.clampDone:
	mov rax, r13 ; result in ax
	
	;------------------------------
.checkResult:  
  ;todo mult sum and compate to divisor
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
.calcRemainderLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	mul r13
	
	sub [r11 + rcx*8 + 8], rax
	sbb [r11 + rcx*8], rdx
	sbb [r11 + rcx*8 - 8], rbx
	jc .calcBorrow ; rare: borrow runs past this word
.calcBorrowDone:

	dec rcx
	jnz .calcRemainderLoop

	;check if reminder is negative
.checkRemainder:
	mov rax, [r11]   ; high word
	test rax, rax
	js .remainder_negative

	;check if the reminder less then divisor
	
	mov rax, [r11+8]
	test rax, rax
	jnz .remainder_bigger
	
	xor rcx, rcx
.checkRemainderLoop:
	mov rax, [r11 + rcx*8 + 16]
	cmp rax, [rsi + rcx*8]
	ja .remainder_bigger
	jb .storeResult
	
	inc rcx
	cmp rcx, r9
	jne .checkRemainderLoop

.remainder_bigger:
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
.fixBiggerLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	
	sub [r11 + rcx*8 + 8], rax
	sbb [r11 + rcx*8], rbx
	sbb [r11 + rcx*8 - 8], rbx
	jc .fixBorrow ; rare: borrow runs past this word
.fixBorrowDone:

	dec rcx
	jnz .fixBiggerLoop

	inc r13

	jmp .checkRemainder ; candidate can be off by more than 1: check again

.remainder_negative:
	
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
.fixNegativeLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	;mul r13
	
	add [r11 + rcx*8 + 8], rax
	adc [r11 + rcx*8], rbx
	adc [r11 + rcx*8 - 8], rbx
	jc .fixCarry ; rare: carry runs past this word
.fixCarryDone:

	dec rcx
	jnz .fixNegativeLoop

	dec r13
	; still negative → add D once more; otherwise it is in [0, D) already
	; (it was in (-D, 0) before adding D), no compare needed
	mov rax, [r11]
	test rax, rax
	js .remainder_negative
.storeResult:
	;store result
	mov [r10], r13
	add r10, 8
.readNexWord:
	;exif if it's the end of dividend
	test r8, r8
	jz .done
	
	; shift reminder
	xor rax, rax
	mov [r11], rax
	xor rcx, rcx
.shiftLoop:
	mov rax, [r11 + rcx*8 + 16]
	mov [r11 + rcx*8 + 8], rax
	inc rcx
	cmp rcx, r9
	jb .shiftLoop
	
	;read and store the next work to lo reminder
	mov rax, [rdi]
	add rdi, 8
	dec r8
	mov [r11 + rcx*8 + 8], rax
	
    jmp .mainLoop

.done:
	ARET rdi, rsi, rbx, r12, r13, r14
;------------------------------------------------------------------------------
; cold paths: a borrow/carry ran past word [r11 + (rcx-1)*8];
; push it further up the window, word by word, until it is absorbed or reaches [r11]
; (rax is free here: it is reloaded at the start of every loop iteration)
;------------------------------------------------------------------------------
.maxOverflow: ; step1 with u2 >= Dhi: resultMax = 2^64 - 1
	mov rax, r13 ; u1 to ax (same state as after the exchange)
	mov r13, -1  ; resultMax
	jmp .minStep

.calcBorrow:
	lea rax, [rcx - 2]
.calcBorrowLoop:
	test rax, rax
	js .calcBorrowDone
	sub qword [r11 + rax*8], 1
	jnc .calcBorrowDone
	dec rax
	jmp .calcBorrowLoop

.fixBorrow:
	lea rax, [rcx - 2]
.fixBorrowLoop:
	test rax, rax
	js .fixBorrowDone
	sub qword [r11 + rax*8], 1
	jnc .fixBorrowDone
	dec rax
	jmp .fixBorrowLoop

.fixCarry:
	lea rax, [rcx - 2]
.fixCarryLoop:
	test rax, rax
	js .fixCarryDone
	add qword [r11 + rax*8], 1
	jnc .fixCarryDone
	dec rax
	jmp .fixCarryLoop

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
