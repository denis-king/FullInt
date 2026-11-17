
; =============================================================================
;  x86 (32-bit) port of x64/Div.asm: the author's division, step by step, on 32-bit
;  registers. cdecl.
;
;  ulong Fullint64DivWin(A, D, Q, R, int lenA, int lenD)   (the return value is unused)
;    remainder R: n + 2 words, [0] sign word, [1 .. n+1] working window, result in [2 .. n+1]
;    quotient  Q: max(m - n + 1, 1) words, MSW first;  D[0] != 0 (normalized divisor)
;
;  The algorithm is the x64 one: compare the window with D (digit 0 / 1), otherwise the digit
;  estimate q_max = u2:u1 / vhi, q_min = u2:u1 / (vhi + 1), interpolation by the next divisor
;  word vlo, clamped to [q_min, q_max]; window -= q·D word by word with the borrow pushed up;
;  corrections (window >= D: subtract D, q + 1; negative: add D, q - 1); shift the window and
;  bring in the next dividend word. What is 64-bit on x64 is built from 32-bit operations:
;    div r64 (128/64)   -> fi_div128 (Div128.asm)
;    mul r64 (64x64)    -> four mul r32
;    shld r64           -> .shld64 below
;    a 64-bit word      -> two dwords, low first ([p], [p + 4])
;
;  Register map (x64 -> x86)
;    rdi dividend item -> esi     (kept in SAVESI while esi is the word index)
;    rsi divisor D     -> edi     r11 remainder window R -> ebp
;    r10 quotient item -> QPTR    r8 dividend words left -> MLEFT    r9 n -> NLEN
;    rcx word index    -> esi (q·D, corrections) / ecx (compare, shift)
;    r13 candidate / max -> QLO:QHI      r12 min -> QMINLO:QMINHI
;    rbx Dhi / vhi, r14 vlo -> VHI*, VLO*      u2, u1 (normalized window top) -> U2*, U1*
; =============================================================================

%include "abi86.inc"
section .text

EXTERNK fi_div128

%define WIN     esp           ; 4 dwords: the 128-bit dividend for fi_div128
%define QPTR    esp+16
%define MLEFT   esp+20
%define NLEN    esp+24
%define QLO     esp+28
%define QHI     esp+32
%define QMINLO  esp+36
%define QMINHI  esp+40
%define VHILO   esp+44
%define VHIHI   esp+48
%define VLOLO   esp+52
%define VLOHI   esp+56
%define U2LO    esp+60
%define U2HI    esp+64
%define U1LO    esp+68
%define U1HI    esp+72
%define SHIFT   esp+76
%define P0      esp+80
%define P3      esp+84
%define SAVESI  esp+88
%define ZERO    esp+92        ; 8 bytes of zero (the divisor word below D[n-1])
%define FRAME   100
%define ARG_A   esp+FRAME+20
%define ARG_D   esp+FRAME+24
%define ARG_Q   esp+FRAME+28
%define ARG_R   esp+FRAME+32
%define ARG_LA  esp+FRAME+36
%define ARG_LD  esp+FRAME+40

; edx:eax = WIN / (%2:%1); the remainder replaces WIN[0..1]. %1, %2 are read before any push.
%macro DIV_BY 2
	mov ecx, %1
	mov ebx, %2
	push ebx
	push ecx
	lea eax, [esp+8]
	push eax
	call KREF(fi_div128)
	add esp, 12
%endmacro

ENTRY Fullint64DivWin
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRAME
	mov esi, [ARG_A]              ; dividend
	mov edi, [ARG_D]              ; divisor
	mov eax, [ARG_Q]
	mov [QPTR], eax               ; result address
	mov ebp, [ARG_R]              ; remainder address
	xor eax, eax
	mov [ZERO], eax
	mov [ZERO+4], eax
	mov eax, [ARG_LA]
	mov [MLEFT], eax
	mov ecx, [ARG_LD]
	mov [NLEN], ecx

	;todo possible we shold clenup the result and remainder buffers
	;so we expect the normilazed divisor!
	cmp eax, ecx
	jae .copyPardDiv

;copy divident to remainder (lenA < lenD: the quotient is 0)
	sub ecx, eax
	lea edx, [ebp + ecx*8]        ; now we skip hi words, the buffer lenth from position is divident + 2
	xor ecx, ecx
.copyDividentToRemainderLoop:
	mov ebx, [esi + ecx*8]
	mov [edx + ecx*8 + 16], ebx
	mov ebx, [esi + ecx*8 + 4]
	mov [edx + ecx*8 + 20], ebx
	inc ecx
	cmp ecx, eax
	jb .copyDividentToRemainderLoop

	;zero the result if its not and exit
	mov edx, [QPTR]
	xor eax, eax
	mov [edx], eax
	mov [edx + 4], eax
	jmp .done

;copy dividend from start and with divisor lenth to thi remainder as a part dividend
.copyPardDiv:
	xor ecx, ecx
.copyLoop:
	mov eax, [esi]
	mov [ebp + ecx*8 + 16], eax
	mov eax, [esi + 4]
	mov [ebp + ecx*8 + 20], eax
	add esi, 8
	dec dword [MLEFT]
	inc ecx
	cmp ecx, [NLEN]
	jb .copyLoop

.mainLoop:
;comparation partDivident and divisor
	mov eax, [ebp + 8]
	or eax, [ebp + 12]
	jnz .findCandidate

	xor ecx, ecx
.comparationLoop:
	mov eax, [ebp + ecx*8 + 20]   ; high dword
	cmp eax, [edi + ecx*8 + 4]
	ja .findCandidate
	jb .cpmpareLess
	mov eax, [ebp + ecx*8 + 16]   ; low dword
	cmp eax, [edi + ecx*8]
	ja .findCandidate
	jb .cpmpareLess
	inc ecx
	cmp ecx, [NLEN]
	jne .comparationLoop

	;we have the equal partDividend and divisor: the result is 1
	mov edx, [QPTR]
	mov dword [edx], 1
	mov dword [edx + 4], 0
	add dword [QPTR], 8
	;clear the reminder buf
	xor ecx, ecx
	xor eax, eax
.clearLoop:
	mov [ebp + ecx*8 + 16], eax
	mov [ebp + ecx*8 + 20], eax
	inc ecx
	cmp ecx, [NLEN]
	jb .clearLoop
	jmp .readNexWord

.cpmpareLess:
	;we have the less partDividend then divisor: the result is 0
	mov edx, [QPTR]
	xor eax, eax
	mov [edx], eax
	mov [edx + 4], eax
	add dword [QPTR], 8
	jmp .readNexWord

.findCandidate:
	cmp dword [NLEN], 1
	ja .calcBit
	;if len divided 1, just div and store: (Uhi:Ulo) / Dhi
	mov eax, [ebp + 16]
	mov [WIN], eax
	mov eax, [ebp + 20]
	mov [WIN + 4], eax
	mov eax, [ebp + 8]
	mov [WIN + 8], eax
	mov eax, [ebp + 12]
	mov [WIN + 12], eax
	DIV_BY [edi], [edi + 4]
	mov ecx, [QPTR]
	mov [ecx], eax                ;store result
	mov [ecx + 4], edx
	add dword [QPTR], 8
	;store remainder into the last window word; .readNexWord stops at the end
	;of the dividend or shifts it up and loads the next word
	xor eax, eax
	mov [ebp + 8], eax            ; Uhi consumed by div
	mov [ebp + 12], eax
	mov eax, [WIN]
	mov [ebp + 16], eax
	mov eax, [WIN + 4]
	mov [ebp + 20], eax
	jmp .readNexWord

.calcBit:
	;calc bit position of Dhi | Uhi (64-bit bsr from the two dwords)
	mov eax, [ebp + 8]
	or eax, [edi]
	mov edx, [ebp + 12]
	or edx, [edi + 4]
	test edx, edx
	jz .bitLow
	bsr ecx, edx
	neg ecx
	add ecx, 31                   ; position from the left side
	jmp .bitDone
.bitLow:
	bsr ecx, eax
	neg ecx
	add ecx, 63
.bitDone:
	mov [SHIFT], ecx

	; --- нормализация dividend ---
	mov eax, [ebp + 8]            ; Uhi
	mov edx, [ebp + 12]
	lea ebx, [ebp + 16]           ; Ulo
	call .shld64
	mov [U2LO], eax               ; новый Uhi
	mov [U2HI], edx
	mov ecx, [SHIFT]
	mov eax, [ebp + 16]           ; Ulo
	mov edx, [ebp + 20]
	lea ebx, [ebp + 24]           ; R[3] (n >= 2 here)
	call .shld64
	mov [U1LO], eax               ; новый Ulo
	mov [U1HI], edx

	; --- нормализация divisor ---
	mov ecx, [SHIFT]
	mov eax, [edi]                ; Dhi
	mov edx, [edi + 4]
	lea ebx, [edi + 8]            ; D1
	call .shld64
	mov [VHILO], eax              ; новый Dhi
	mov [VHIHI], edx
	mov ecx, [SHIFT]
	lea ebx, [ZERO]               ; D2 = 0 when n == 2
	cmp dword [NLEN], 2
	je .shiftDlo
	lea ebx, [edi + 16]           ; D2
.shiftDlo:
	mov eax, [edi + 8]            ; D1
	mov edx, [edi + 12]
	call .shld64
	mov [VLOLO], eax              ; новый Dlo
	mov [VLOHI], edx

;step1: max = u2:u1 / vhi, or 2^64 - 1 when u2 >= vhi (div r64 would raise #DE)
	mov eax, [U2HI]
	cmp eax, [VHIHI]
	ja .maxOverflow
	jb .maxDiv
	mov eax, [U2LO]
	cmp eax, [VHILO]
	jae .maxOverflow
.maxDiv:
	mov eax, [U1LO]
	mov [WIN], eax
	mov eax, [U1HI]
	mov [WIN + 4], eax
	mov eax, [U2LO]
	mov [WIN + 8], eax
	mov eax, [U2HI]
	mov [WIN + 12], eax
	DIV_BY [VHILO], [VHIHI]
	mov [QLO], eax                ; resultMax
	mov [QHI], edx
	jmp .minStep
.maxOverflow:
	mov dword [QLO], -1
	mov dword [QHI], -1

.minStep: ; min = u2:u1 / (vhi + 1), or u2 when vhi + 1 = 2^64
	mov eax, [U1LO]
	mov [WIN], eax
	mov eax, [U1HI]
	mov [WIN + 4], eax
	mov eax, [U2LO]
	mov [WIN + 8], eax
	mov eax, [U2HI]
	mov [WIN + 12], eax
	mov eax, [VHILO]
	mov edx, [VHIHI]
	add eax, 1                    ; d2 + 1
	adc edx, 0
	jc .minIsU2
	DIV_BY eax, edx
	mov [QMINLO], eax             ; min result
	mov [QMINHI], edx
	jmp .step2
.minIsU2:
	mov eax, [U2LO]
	mov [QMINLO], eax
	mov eax, [U2HI]
	mov [QMINHI], eax

;step2: interpolate by the next divisor word, clamp to [min, max]
.step2:
	mov eax, [QMINLO]
	cmp eax, [QLO]
	jne .interpolate
	mov eax, [QMINHI]
	cmp eax, [QHI]
	je .checkResult
.interpolate:
	mov eax, [QLO]                ; range = max - min + 1
	mov edx, [QHI]
	sub eax, [QMINLO]
	sbb edx, [QMINHI]
	add eax, 1
	adc edx, 0
	mov ecx, eax
	or ecx, edx
	jz .checkResult               ; range = 2^64 (max = 2^64-1, min = 0): keep max, corrections below fix it
	mov dword [WIN], -1           ; step = (2^64 - 1) / range
	mov dword [WIN + 4], -1
	mov dword [WIN + 8], 0
	mov dword [WIN + 12], 0
	DIV_BY eax, edx
	mov ecx, eax                  ; step
	mov ebx, edx
	mov eax, [VLOLO]              ; offset = vlo / step
	mov [WIN], eax
	mov eax, [VLOHI]
	mov [WIN + 4], eax
	mov dword [WIN + 8], 0
	mov dword [WIN + 12], 0
	DIV_BY ecx, ebx
	mov ecx, [QLO]                ; max - offset
	mov ebx, [QHI]
	sub ecx, eax
	sbb ebx, edx
	jc .clampMin                  ; offset > max
	cmp ebx, [QMINHI]
	ja .clampDone
	jb .clampMin
	cmp ecx, [QMINLO]
	jae .clampDone
.clampMin:
	mov ecx, [QMINLO]             ; never below min: the true digit is in [min, max]
	mov ebx, [QMINHI]
.clampDone:
	mov [QLO], ecx
	mov [QHI], ebx

;------------------------------
.checkResult: ; window -= q · D, word by word; the borrow of each word goes into R[i], rarely further
	mov [SAVESI], esi
	mov esi, [NLEN]
	dec esi                       ; i = n - 1 .. 0: D[i] at [edi + i*8], its window word R[i + 2]
.calcRemainderLoop:
	mov eax, [edi + esi*8]        ; D[i] · q = P3:P2:P1:P0
	mul dword [QLO]
	mov [P0], eax
	mov ebx, edx                  ; P1
	mov eax, [edi + esi*8]
	mul dword [QHI]
	add ebx, eax
	mov ecx, edx                  ; P2
	adc ecx, 0
	mov eax, [edi + esi*8 + 4]
	mul dword [QLO]
	add ebx, eax
	adc ecx, edx
	mov dword [P3], 0
	adc dword [P3], 0
	mov eax, [edi + esi*8 + 4]
	mul dword [QHI]
	add ecx, eax
	adc [P3], edx

	mov eax, [P0]
	sub [ebp + esi*8 + 16], eax   ; R[i + 2] -= low half
	sbb [ebp + esi*8 + 20], ebx
	mov eax, [P3]
	sbb [ebp + esi*8 + 8], ecx    ; R[i + 1] -= high half
	sbb [ebp + esi*8 + 12], eax
	sbb dword [ebp + esi*8], 0    ; R[i] -= borrow
	sbb dword [ebp + esi*8 + 4], 0
	jc .calcBorrow                ; rare: borrow runs past this word
.calcBorrowDone:
	dec esi
	jns .calcRemainderLoop

	;check if reminder is negative
.checkRemainder:
	mov eax, [ebp + 4]            ; high word, high dword
	test eax, eax
	js .remainder_negative

	;check if the reminder less then divisor
	mov eax, [ebp + 8]
	or eax, [ebp + 12]
	jnz .remainder_bigger

	xor ecx, ecx
.checkRemainderLoop:
	mov eax, [ebp + ecx*8 + 20]
	cmp eax, [edi + ecx*8 + 4]
	ja .remainder_bigger
	jb .storeResult
	mov eax, [ebp + ecx*8 + 16]
	cmp eax, [edi + ecx*8]
	ja .remainder_bigger
	jb .storeResult
	inc ecx
	cmp ecx, [NLEN]
	jne .checkRemainderLoop

.remainder_bigger:
	mov esi, [NLEN]
	dec esi
.fixBiggerLoop:
	mov eax, [edi + esi*8]        ;divisor
	sub [ebp + esi*8 + 16], eax
	mov eax, [edi + esi*8 + 4]
	sbb [ebp + esi*8 + 20], eax
	sbb dword [ebp + esi*8 + 8], 0
	sbb dword [ebp + esi*8 + 12], 0
	sbb dword [ebp + esi*8], 0
	sbb dword [ebp + esi*8 + 4], 0
	jc .fixBorrow                 ; rare: borrow runs past this word
.fixBorrowDone:
	dec esi
	jns .fixBiggerLoop

	add dword [QLO], 1
	adc dword [QHI], 0
	jmp .checkRemainder           ; candidate can be off by more than 1: check again

.remainder_negative:
	mov esi, [NLEN]
	dec esi
.fixNegativeLoop:
	mov eax, [edi + esi*8]        ;divisor
	add [ebp + esi*8 + 16], eax
	mov eax, [edi + esi*8 + 4]
	adc [ebp + esi*8 + 20], eax
	adc dword [ebp + esi*8 + 8], 0
	adc dword [ebp + esi*8 + 12], 0
	adc dword [ebp + esi*8], 0
	adc dword [ebp + esi*8 + 4], 0
	jc .fixCarry                  ; rare: carry runs past this word
.fixCarryDone:
	dec esi
	jns .fixNegativeLoop

	sub dword [QLO], 1
	sbb dword [QHI], 0
	; still negative → add D once more; otherwise it is in [0, D) already
	mov eax, [ebp + 4]
	test eax, eax
	js .remainder_negative
.storeResult:
	mov esi, [SAVESI]
	mov ecx, [QPTR]
	mov eax, [QLO]
	mov [ecx], eax
	mov eax, [QHI]
	mov [ecx + 4], eax
	add dword [QPTR], 8
.readNexWord:
	;exif if it's the end of dividend
	cmp dword [MLEFT], 0
	je .done

	; shift reminder
	xor eax, eax
	mov [ebp], eax
	mov [ebp + 4], eax
	xor ecx, ecx
.shiftLoop:
	mov eax, [ebp + ecx*8 + 16]
	mov [ebp + ecx*8 + 8], eax
	mov eax, [ebp + ecx*8 + 20]
	mov [ebp + ecx*8 + 12], eax
	inc ecx
	cmp ecx, [NLEN]
	jb .shiftLoop

	;read and store the next work to lo reminder
	mov eax, [esi]
	mov [ebp + ecx*8 + 8], eax
	mov eax, [esi + 4]
	mov [ebp + ecx*8 + 12], eax
	add esi, 8
	dec dword [MLEFT]
	jmp .mainLoop

.done:
	add esp, FRAME
	xor eax, eax
	xor edx, edx
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret

;------------------------------------------------------------------------------
; cold paths: a borrow/carry ran past word R[i] (esi = i);
; push it further up the window, word by word, until it is absorbed or reaches R[0]
;------------------------------------------------------------------------------
.calcBorrow:
	lea eax, [esi - 1]
.calcBorrowLoop:
	test eax, eax
	js .calcBorrowDone
	sub dword [ebp + eax*8], 1
	sbb dword [ebp + eax*8 + 4], 0
	jnc .calcBorrowDone
	dec eax
	jmp .calcBorrowLoop

.fixBorrow:
	lea eax, [esi - 1]
.fixBorrowLoop:
	test eax, eax
	js .fixBorrowDone
	sub dword [ebp + eax*8], 1
	sbb dword [ebp + eax*8 + 4], 0
	jnc .fixBorrowDone
	dec eax
	jmp .fixBorrowLoop

.fixCarry:
	lea eax, [esi - 1]
.fixCarryLoop:
	test eax, eax
	js .fixCarryDone
	add dword [ebp + eax*8], 1
	adc dword [ebp + eax*8 + 4], 0
	jnc .fixCarryDone
	dec eax
	jmp .fixCarryLoop

;------------------------------------------------------------------------------
; .shld64: edx:eax = (X << s) | (Y >> (64 - s)), the x64 `shld r64, r64, cl`
;   in: edx:eax = X (high:low), ebx -> Y (8 bytes), cl = s (0..63); keeps ebx, ecx, esi, edi, ebp
;   cl >= 32 works as s - 32 (x86 takes 32-bit shift counts mod 32)
;------------------------------------------------------------------------------
.shld64:
	push esi
	mov esi, [ebx + 4]            ; Y high
	cmp cl, 32
	jae .shldHigh
	shld edx, eax, cl             ; high = (Xhi, Xlo)
	shld eax, esi, cl             ; low  = (Xlo, Yhi)
	pop esi
	ret
.shldHigh:
	mov edx, eax
	shld edx, esi, cl             ; high = (Xlo, Yhi)
	mov eax, esi
	mov esi, [ebx]
	shld eax, esi, cl             ; low  = (Yhi, Ylo)
	pop esi
	ret
