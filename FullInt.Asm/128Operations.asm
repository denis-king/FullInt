PUBLIC Div128
PUBLIC Mul128
PUBLIC Add64
PUBLIC Sub64
PUBLIC Adc64
PUBLIC Sbb64
PUBLIC Normalize3Words
PUBLIC Fullint64Add
PUBLIC Fullint64Sub
PUBLIC Fullint64Mult
PUBLIC Fullint64Div
.code

; ------------------------------------------------------------
; uint64 Mul64(uint64 a, uint64 b, ref uint64 high)
; RCX = a, RDX = b, R8 = pointer to high
; Returns: RAX = low part, [R8] = high part
Mul128 PROC
    mov rax, rcx        ; a → RAX
    mul rdx             ; RAX * RDX → RDX:RAX
    mov [r8], rdx       ; high part → *high
    ret
Mul128 ENDP

; ------------------------------------------------------------
; uint64 Div128(uint64 hi, uint64 lo, uint64 divisor, ref uint64 remainder)
; RCX = hi, RDX = lo, R8 = divisor, R9 = pointer to remainder
; Returns: RAX = quotient, [R9] = remainder
Div128 PROC
    mov r10, r8         ; divisor → R10
    mov rax, rdx        ; lo → RAX
    mov rdx, rcx        ; hi → RDX
    div r10             ; RDX:RAX / R10 → RAX = quotient, RDX = remainder
    mov [r9], rdx       ; remainder → *remainder
    ret
Div128 ENDP

; ------------------------------------------------------------
; uint64 Add64(uint64 a, uint64 b, ref byte carry)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
Add64 PROC
    add rcx, rdx		; rax = a + b + CF
    setc r9b			; r9 = new CF 
    mov byte ptr [r8], r9b	; carryOut = r9
    ret
Add64 ENDP


; ------------------------------------------------------------
; uint64 Adc64(uint64 a, uint64 b, ref byte carry)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
Adc64 PROC
    
    movzx r9, byte ptr [r8]	; r9 = carryIn
    shr r9b, 1			; CF = r9

    mov rax, rcx		; rax = a
    adc rax, rdx		; rax = a + b + CF
    setc r9b			; r9 = new CF
   
    mov byte ptr [r8], r9b	; carryOut = r9
    ret
Adc64 ENDP

; ------------------------------------------------------------
; uint64 Sub64(uint64 a, uint64 b, ref byte borrow)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
Sub64 PROC
    sub rax, rdx		; rax = a - b - CF
    setc r9b			; r9 = new CF
    mov byte ptr [r8], r9b	; borrowOut = r9
    ret
Sub64 ENDP

; ------------------------------------------------------------
; uint64 Sbb64(uint64 a, uint64 b, ref byte borrow)
; RCX = a, RDX = b, R8 = carry*
; Returns: RAX = result, carry updates by ref
Sbb64 PROC
    
    movzx r9, byte ptr [r8]	; r9 = borrowIn
    shr r9b, 1			; CF = r9

    mov rax, rcx		; rax = a
    sbb rax, rdx		; rax = a - b - CF
    setc r9b			; r9 = new CF
   
    mov byte ptr [r8], r9b	; borrowOut = r9
    ret
Sbb64 ENDP

Normalize3Words PROC

    ; Вход:
    ; rcx = &D2
    ; rdx = &D1
    ; r8  = D0
    ; r9  = &U2
    ; [rsp+28h] = &U1
    ; [rsp+30h] = U0
	
	mov r15, [rcx] 
	mov r10, [r9]
	or  r15, r10 ; D2 | U2
	
    ; --- загрузка значений ---
    mov rax, [rcx]       ; D2
    mov rbx, [rdx]       ; D1
    mov r10, rcx         ; d2 address
    mov r11, rdx         ; d1 address
    mov r12, [rsp+28h]   ; &U1 address
    mov r13, [r12]       ; U1
    mov r14, [rsp+30h]   ; U0
	
    ; --- вычисление shiftLen ---
	xor rcx, rcx
	xor rdx, rdx
	
    bsr rdx, r15         ; индекс старшего бита D2
    mov rcx, 63
    sub rcx, rdx         ; shiftLen
	
    ; --- нормализация делителя ---
    shld rax, rbx, cl    ; новый D2
    shld rbx, r8, cl    ; новый D1
	
	mov r15, [r9] 		; U2
    ; --- нормализация делимого ---
    shld r15, r13, cl    ; новый U2
    shld r13, r14, cl    ; новый U1

	;-----------------------------
	cmp r15, rax
	jae temp
	
	mov rcx, rax ; d2 to cx
	mov rdx, r15 ; u2
	mov rax, r13 ; u1
	div rcx
	
	;exchange rax and r13
	xor rax, r13
	xor r13, rax ; resultMax to r13
	xor rax, r13 ; u1 to ax
	
	mov rdx, r15 ; u2
	inc rcx ; d2 + 1
	div rcx
	;resultMin in ax 
	jmp temp2
 temp:
	mov rcx, rax ; d2 to cx
	xor rdx, rdx ; 0
	mov rax, r15 ; u2
	div rcx
	mov r13, rax ; resultMax to r13
	
	xor rdx, rdx ; 0
	mov rax, r15 ; u2
	inc rcx ; d2 + 1
	div rcx
	;resultMin in ax 
	
 temp2:
	cmp rax, r13
	je exit
	
	mov rcx, r13 ; max to cx
	sub rcx, rax ; max - min
	inc rcx ; range
	xor rdx, rdx 
	mov rax, 0ffffffffffffffffh ;ulong max value
	div rcx ; 
	; step in ax
	mov rcx, rax ; step in cx
	mov rax, rbx 
	xor rdx, rdx
	div rcx ; range offset in ax
	
	sub r13, rax ; max - offset
	mov rax, r13 ; result in ax
  
  ; --- запись результатов обратно ---
  ;  mov [r10], rax       ; обновить D2
  ;  mov [r11], rbx       ; обновить D1
  ;  mov [r9],  r15        ; обновить U2
  ;  mov [r12], r13       ; обновить U1
	
	;xor rax, rax
	;mov al, cl
exit:    ret
Normalize3Words ENDP



;WINDOWS wrapper
Fullint64AddWIN PROC
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	mov rdx, r9
	mov r8, [rsp+28h]
	mov r9, [rsp+30h]
	
	call Fullint64Add
	ret
Fullint64AddWIN ENDP

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW
Fullint64Add PROC
	
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    lea r11, [rsi + r8*8 - 8]     ; ptrB = baseB + lenB*8
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8

    ; Перенос
    xor eax, eax              ; AL = 0
   ; clc                       ; CF = 0

iteration:
   
    ; Загрузить слово A или 0
    xor r13, r13
    cmp r10, rdi              ; ptrA > baseA ? есть слова
    jl skipA
    mov r13, [r10]  ; взять A[lenA-1] по текущему адресу
skipA:

    ; Загрузить слово B или 0
    xor r14, r14
    cmp r11, rsi
    jl skipB
    mov r14, [r11]
skipB:
	
	  ; Восстановить CF из AL (AL бит0 > CF)
    shr al, 1
    ; Сложить A + B и добавить перенос ровно одним adc
    adc r13, r14
	; Сохранить CF в AL, прежде чем портить флаги
    setc al
    ; Записать в R[lenR-1] по адресу (ptrR)
    mov [r12], r13 

   

    ; Если результат исчерпан — выход
    cmp r12, rcx ;todo сравнить адрес с базовим если равны то выход
    je done
    sub r12, 8

    ; Продвинуть адрес A, если ещё есть слова
    ;cmp r10, rdi
    ;jbe .skipDecA
    sub r10, 8
;skipDecA:

    ; Продвинуть адрес B, если ещё есть слова
    ; cmp r11, rsi
    ; jbe .skipDecB
    sub r11, 8
;skipDecB:

    jmp iteration

done:
    ret
Fullint64Add ENDP


;WINDOWS wrapper
Fullint64SubWIN PROC
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	mov rdx, r9
	mov r8, [rsp+28h]
	mov r9, [rsp+30h]
	
	call Fullint64Sub
	ret
Fullint64SubWIN ENDP

; void Fullint64Add(
;   const uint64_t* A, size_t lenA,
;   const uint64_t* B, size_t lenB,
;   uint64_t* R,     size_t lenR )
; rdi=A, rsi=B, rcx=R, 
; rdx=LenA, r8=lenB, r9=lenR
; порядок: index 0 = MSW, index len-1 = LSW
Fullint64Sub PROC

skipZerosA:
	mov rax, [rdi]
	test rax, rax
	jnz skipZerosB
	cmp rdx, 1
	je skipZerosB
	add rdi, 8
	dec rdx
	jmp skipZerosA
	
skipZerosB:
	mov rax, [rsi]
	test rax, rax
	jnz compare
	cmp r8, 1
	je compare
	add rsi, 8
	dec r8
	jmp skipZerosB
	
	
compare:	
	mov r15, 1
	cmp rdx, r8
	ja init
	jb swap
	
	xor rbx, rbx
comparationLoop:
	mov rax, [rdi + rbx*8]
	cmp rax, [rsi + rbx*8]
	ja init
	jb swap
	
	inc rbx
	cmp rbx, rdx
	jne comparationLoop
	
	;we have equal A and B
	xor rax, rax
	xor r15, r15

zeroResultLoop:
	mov [rcx], rax
	add rcx, 8
	dec r9
	jnz zeroResultLoop
	jmp done
	
swap:	
	xor rdi, rsi
	xor rsi, rdi
	xor rdi, rsi
	
	xor rdx, r8
	xor r8, rdx
	xor rdx, r8
	
	mov r15, -1
init:
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    lea r11, [rsi + r8*8 - 8]     ; ptrB = baseB + lenB*8
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8
	
    xor rax, rax              ; AL = 0

iteration:
   
    ; Загрузить слово A или 0
    xor r13, r13
    cmp r10, rdi              ; ptrA > baseA ? есть слова
    jl skipA
    mov r13, [r10]  ; взять A[lenA-1] по текущему адресу
skipA:

    ; Загрузить слово B или 0
    xor r14, r14
    cmp r11, rsi
    jl skipB
    mov r14, [r11]
skipB:
	
	  ; Восстановить CF из AL (AL бит0 > CF)
    shr al, 1
    ; Сложить A + B и добавить перенос ровно одним adc
    sbb r13, r14
	; Сохранить CF в AL, прежде чем портить флаги
    setc al
    ; Записать в R[lenR-1] по адресу (ptrR)
    mov [r12], r13 

    ; Если результат исчерпан — выход
    cmp r12, rcx ;todo сравнить адрес с базовим если равны то выход
    je done
    sub r12, 8
    sub r10, 8
    sub r11, 8

    jmp iteration

done:
	mov rax, r15
    ret
Fullint64Sub ENDP


Fullint64MultWIN PROC
	;transfor the windows ABI to the Linux/Mac ABI
	mov rdi, rcx
	mov rsi, rdx
	mov rcx, r8
	mov rdx, r9
	mov r8, [rsp+28h]
	mov r9, [rsp+30h]
	
	call Fullint64Mult
	ret
Fullint64MultWIN ENDP

Fullint64Mult PROC
    ; Инициализация адресных счётчиков: ставим на "выход за конец"
    lea r10, [rdi + rdx*8 - 8]    ; ptrA = baseA + lenA*8 (указатель на байт после последнего слова)
    mov rbx, r10 ;copy the latest position
    lea r12, [rcx + r9*8 - 8]     ; ptrR = baseR + lenR*8

	xor r13, r13
	xor r14, r14
	xor r15, r15
	
outerIteration:
	lea r11, [rsi + r8*8 - 8]; set the latest B postion
	mov r9, r10; restore shifted A item with current A iter
innerIteration:
	
	;skip if shifted A item is out of A range
	cmp r9, rdi
	jl endInnerIteration
	
	cmp r9, rbx
	jg endInnerIteration
	
	;mult ax to dx => dx:ax
	mov rax, [r9]
	mov rdx, [r11]
	mul rdx
	
	;add the result
	clc
	adc r13, rax ; addLo
	adc r14, rdx ; addMid
	adc r15, 0 ; addHi
	
	
endInnerIteration:	
	add r9, 8; shift A index + B index
	
	;inner loop: B lenth
	cmp r11, rsi ;todo сравнить адрес с базовим если равны то выход
    je innerDone
    sub r11, 8;move B index to the next position
	jmp innerIteration

innerDone:	
	mov [r12], r13 ;store the result
	mov r13, r14 ; mid to lo
	mov r14, r15 ; hi to mid
	xor r15, r15 ; zoro hi
	
	sub r10, 8; move A index to the next position
	;outer loop result lenth
	cmp r12, rcx ;todo сравнить адрес с базовим если равны то выход
    je done
    sub r12, 8
	jmp outerIteration
done:
	ret
Fullint64Mult ENDP

Fullint64DivWin PROC
	mov rdi, rcx ;dividend
	mov rsi, rdx ;divisor
	mov rcx, r8 ;result len r8 - r9 + 1 and atleast 1
	mov rdx, r9 ;remainder len r8 + 2
	mov r8, [rsp+28h] ;lenDividend
	mov r9, [rsp+30h] ;lenDivisor
	
	call Fullint64Div
	ret
Fullint64DivWin ENDP

Fullint64Div PROC
	
	mov r10, rcx ; result address
	mov r11, rdx ; remainder address
   
   ;todo possible we shold clenup the result and remainder buffers
   ;todo possible we shold normileze the divisor buf to skip hi zeros, but it will cause increase the result lenth
   ;so we expect the normilazed divisor!
   
	cmp r8, r9
	jae copyPardDiv
	

	
;copy divident to remainder
	mov rax, r9
	sub rax, r8
	shl rax, 3 ; mul 8                                                                                                
	add r11, rax ; now we skipe hi words, the buffer lenth from position is divident + 2
copyDividentToRemainderLoop: 
	mov rax, [rdi + rcx*8]
	mov [r11+rcx*8+16], rax
	inc rcx
	cmp rcx, r8
	jb copyDividentToRemainderLoop
	
	;zero the result if its not and exit
	xor rax, rax
	mov [r10], rax

	jmp done

;copy dividend from start and with divisor lenth to thi remainder as a part dividend
copyPardDiv:	
	xor rcx, rcx
copyLoop: 
	mov rax, [rdi]
	mov [r11+rcx*8+16], rax
	add rdi, 8
	dec r8
	inc rcx
	cmp rcx, r9
	jb copyLoop
	

mainLoop:
;comparation partDivident and divisor
	mov rax, [r11+8]
	test rax, rax
	jnz findCandidate
	
	xor rcx, rcx
comparationLoop:
	mov rax, [r11 + rcx*8 + 16]
	cmp rax, [rsi + rcx*8]
	ja findCandidate
	jb cpmpareLess
	
	inc rcx
	cmp rcx, r9
	jne comparationLoop
	jmp cpmpareEqual
cpmpareLess:	
	;we have the less partDividend then divisor
	;so the result is 0
	xor rax, rax
	mov [r10], rax
	add r10, 8
	jmp readNexWord
	
	
cpmpareEqual:	
	cmp rcx, r9
	jb comparationLoop
	;we have the equal partDividend and divisor
	;so the result is 1
	mov rax, 1
	mov [r10], rax
	add r10, 8
	
	;clear the reminder buf
	xor rcx, rcx
clearLoop:
	xor rax, rax
	mov [r11 + rcx*8 + 16], rax
	inc rcx
	cmp rcx, r9
	jb clearLoop
	
	jmp readNexWord

findCandidate:

	xor rcx, rcx
	;todo check lenth of the D and U buffers
	mov rax, [r11 + 8] ; Uhi
	test rax, rax
	jnz getU2
	mov rax, [r11 + 16] ; Ulo
	xor rdx, rdx ;Uhi
	mov rcx, rax
	jmp getD1
getU2:
	mov rdx, rax ; Uhi
	mov rax, [r11 + 16] ;Ulo
	mov rcx, rdx
getD1:
	mov rbx, [rsi] ; Dhi
	
	cmp r9, 1
	ja calcBit
	;if len divided 1, just div and store
	div rbx
	;store result
	mov [r10], rax
	add r10, 8
	;store remainder
	mov [r11 + 8], rdx
	
	;read and store the next work to lo reminder
	mov rax, [rdi]
	add rdi, 8
	dec r8
	mov [r11 + 16], rax
	
    jmp mainLoop
	 
calcBit:	
	;calc bit position
	or rcx, rbx ; Dhi or Uhi
	bsr rcx, rcx ; calc bit position
	neg rcx
	add rcx, 63 ; get position from the left side
	
    ; --- нормализация dividend ---
    shld rdx, rax, cl    ; новый Uhi
	xor r12, r12
	cmp r9, 1
	je shistUlo
	mov r12, [r11 + 24]
shistUlo:	
	shld rax, r12, cl    ; новый Ulo
    mov r12, rdx
	mov r13, rax
	
	;todo move the norbalization before loop
    ; --- нормализация divisor ---
	xor r14, r14
	xor rax, rax
	cmp r9, 2
	jb shiftDhi
	mov r14, [rsi + 8]
	je shiftDhi
	mov rax, [rsi + 16]
	
shiftDhi:	
    shld rbx, r14, cl    ; новый Dhi
    shld r14, rax, cl    ; новый Dlo

;step1	
	mov rdx, r12 ; u2
	mov rax, r13 ; u1
	div rbx
	
	;exchange rax and r13
	xor rax, r13
	xor r13, rax ; resultMax to r13
	xor rax, r13 ; u1 to ax
	
	mov rdx, r12 ; u2
	
	inc rbx ; d2 + 1
	div rbx
	
	mov r12, rax ; min result
	
	
;step2
	cmp r12, r13
	je checkResult
	
	mov rcx, r13 ; max to cx
	sub rcx, r12 ; max - min
	inc rcx ; range
	xor rdx, rdx 
	mov rax, 0ffffffffffffffffh ;ulong max value
	div rcx ; 
	; step in ax
	mov rcx, rax ; step in cx
	
	xor rdx, rdx
	mov rax, r14 ; Ulo
	
	div rcx ; range offset in ax
	
	sub r13, rax ; max - offset
	mov rax, r13 ; result in ax
	
	;------------------------------
checkResult:  
  ;todo mult sum and compate to divisor
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
calcRemainderLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	mul r13
	
	sub [r11 + rcx*8 + 8], rax
	sbb [r11 + rcx*8], rdx
	sbb [r11 + rcx*8 - 8], rbx
	
	dec rcx
	jnz calcRemainderLoop
	
	;check if reminder is negative
	mov rax, [r11]   ; high word
	test rax, rax
	js remainder_negative

	;check if the reminder less then divisor
	
	mov rax, [r11+8]
	test rax, rax
	jnz remainder_bigger
	
	xor rcx, rcx
checkRemainderLoop:
	mov rax, [r11 + rcx*8 + 16]
	cmp rax, [rsi + rcx*8]
	ja remainder_bigger
	jb storeResult
	
	inc rcx
	cmp rcx, r9
	jne checkRemainderLoop

remainder_bigger:
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
fixBiggerLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	
	sub [r11 + rcx*8 + 8], rax
	sbb [r11 + rcx*8], rbx 
	sbb [r11 + rcx*8 - 8], rbx
	
	dec rcx
	jnz fixBiggerLoop  
	
	inc r13

	jmp storeResult	

remainder_negative:
	
	mov rcx, r9
	xor rdx, rdx
	xor rbx, rbx
fixNegativeLoop:  
	
	mov rax, [rsi + rcx*8 - 8] ;divisor
	;mul r13
	
	add [r11 + rcx*8 + 8], rax
	adc [r11 + rcx*8], rbx 
	adc [r11 + rcx*8 - 8], rbx
	
	dec rcx
	jnz fixNegativeLoop  
	
	dec r13
storeResult:
	;store result
	mov [r10], r13
	add r10, 8
readNexWord:
	;exif if it's the end of dividend
	test r8, r8
	jz done
	
	; shift reminder
	xor rax, rax
	mov [r11], rax
	xor rcx, rcx
shiftLoop:
	mov rax, [r11 + rcx*8 + 16]
	mov [r11 + rcx*8 + 8], rax
	inc rcx
	cmp rcx, r9
	jb shiftLoop
	
	;read and store the next work to lo reminder
	mov rax, [rdi]
	add rdi, 8
	dec r8
	mov [r11 + rcx*8 + 8], rax
	
    jmp mainLoop

done:
	ret
Fullint64Div ENDP
END