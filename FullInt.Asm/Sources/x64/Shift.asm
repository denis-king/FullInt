
default rel
%include "abi.inc"
section .text

; =============================================================================
;  Magnitude shifts. Limb order: index 0 = most significant word.
;  Signs (and floor rounding of negative >>) are handled by the caller.
;
;  shift = ws * 64 + s     (ws whole words, s = 0..63 bits)
;
;  Shl: R = A << shift,  lenR >= lenA + ws + 1
;       R = [lead zeros][A shifted: lenA + 1 words][ws zeros]
;  Shr: R = A >> shift,  lenR >= max(lenA - ws, 1);  R may alias A (with ws >= 1 low end first)
;       R = [lead zeros][A[0 .. lenA-ws-1] shifted right by s]
;
;  void Fullint64ShlWIN(A, R, int lenA, int lenR, int shift)   (Windows and System V: one entry each,
;  abi.inc; the bodies take rdi = A, rsi = R, rcx = lenA, rdx = lenR, r8 = shift)
;
;  The bodies use only rax, rcx, rdx, rsi, rdi, r8-r11 (volatile in System V).
; =============================================================================


ENTRY Fullint64ShlWIN
	SAVE rdi, rsi
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	movsxd rcx, r8d
	movsxd rdx, r9d
	movsxd r8, dword STACKARG(5, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd rcx, ecx
	movsxd rdx, edx
	movsxd r8, r8d
%endif
Fullint64Shl_body:
	mov r9, rcx                 ; lenA
	mov r10, rdx                ; lenR
	mov r11, r8
	shr r11, 6                  ; ws
	mov rcx, r8
	and ecx, 63                 ; s

	; lead = lenR - (lenA + 1 + ws)
	mov rax, r10
	sub rax, r9
	sub rax, r11
	dec rax
	xor edx, edx
.lead:
	test rax, rax
	jle .shift
	mov [rsi], rdx
	add rsi, 8
	dec rax
	jmp .lead

.shift:                         ; rdx = previous (more significant) word, 0 at the top
	test r9, 1
	jz .even
	; an odd word first, one shld step (any s, 0 included)
	mov rax, [rdi]
	mov r8, rdx
	shld r8, rax, cl            ; (prev << s) | (cur >> (64 - s))
	mov [rsi], r8
	mov rdx, rax
	add rdi, 8
	add rsi, 8
	dec r9
	jz .lowest
.even:
	test ecx, ecx
	jz .copy                    ; s = 0: whole words only

	; two words per step with shl/shr/or (shld r, r, cl is several uops and slow on
	; recent cores). The shift unit takes the count mod 64, so after neg ecx cl acts as
	; 64 - s; s > 0 here. r10 (lenR) is free after the lead.
	align 32                      ; the hot loop on a line of its own (abi.inc)
.pairs:
	mov rax, [rdi]              ; w0
	mov r8, [rdi + 8]           ; w1
	shl rdx, cl                 ; prev << s
	mov r10, rax
	shl r10, cl                 ; w0 << s
	neg ecx                     ; cl = 64 - s
	shr rax, cl                 ; w0 >> (64 - s)
	or rdx, rax                 ; out0
	mov rax, r8
	shr rax, cl                 ; w1 >> (64 - s)
	or r10, rax                 ; out1
	neg ecx                     ; cl = s
	mov [rsi], rdx
	mov [rsi + 8], r10
	mov rdx, r8                 ; prev = w1
	add rdi, 16
	add rsi, 16
	sub r9, 2
	jnz .pairs

.lowest:
	shl rdx, cl                 ; lowest word
	mov [rsi], rdx
	add rsi, 8

	xor eax, eax
.tail:                          ; ws zero words
	test r11, r11
	jz .done
	mov [rsi], rax
	add rsi, 8
	dec r11
	jmp .tail
.done:
	ARET rdi, rsi
.copy:                          ; s = 0: out = prev
	mov rax, [rdi]
	mov [rsi], rdx
	mov rdx, rax
	add rdi, 8
	add rsi, 8
	dec r9
	jnz .copy
	jmp .lowest

;------------------------------------------------------------------------------

ENTRY Fullint64ShrWIN
	SAVE rdi, rsi
%ifdef FULLINT_WIN64
	mov rdi, rcx
	mov rsi, rdx
	movsxd rcx, r8d
	movsxd rdx, r9d
	movsxd r8, dword STACKARG(5, FI_NPUSH)
%else
	xchg rcx, rdx
	movsxd rcx, ecx
	movsxd rdx, edx
	movsxd r8, r8d
%endif
Fullint64Shr_body:
	mov r9, rcx                 ; lenA
	mov r10, rdx                ; lenR
	mov r11, r8
	shr r11, 6                  ; ws
	mov rcx, r8
	and ecx, 63                 ; s

	mov rax, r9
	sub rax, r11
	jbe .allZero                ; lenA <= ws: everything shifted out
	mov r9, rax                 ; count = lenA - ws source words

	; R over A with whole words shifted out (ws >= 1): this top-down pass would overwrite
	; words of A before reading them (the lead zeros, and every store ws words up) - that case
	; goes low end first (.inPlace). R apart from A, or ws = 0: as before.
	test r11, r11
	jz .topDown
	lea rax, [r9 + r11]
	lea rax, [rdi + rax*8]      ; the end of A
	cmp rsi, rax
	jae .topDown
	lea rax, [rsi + r10*8]      ; the end of R
	cmp rdi, rax
	jb .inPlace
.topDown:
	mov rax, r10
	sub rax, r9                 ; lead = lenR - count
	xor edx, edx
.lead:
	test rax, rax
	jle .shift
	mov [rsi], rdx
	add rsi, 8
	dec rax
	jmp .lead

.shift:                         ; rdx = previous (more significant) word, 0 at the top
	test r9, 1
	jz .even
	; an odd word first, one shrd step (any s, 0 included)
	mov rax, [rdi]
	mov r8, rax
	shrd r8, rdx, cl            ; (cur >> s) | (prev << (64 - s))
	mov [rsi], r8
	mov rdx, rax
	add rdi, 8
	add rsi, 8
	dec r9
	jnz .even
	ARET rdi, rsi
.even:
	test ecx, ecx
	jz .copy                    ; s = 0: whole words only

	; two words per step, as in Shl; both words are read before either is written, so
	; R may still alias A. r10 (lenR) is free here.
	align 32                      ; the hot loop on a line of its own (abi.inc)
.pairs:
	mov rax, [rdi]              ; w0
	mov r8, [rdi + 8]           ; w1
	mov r10, rax
	neg ecx                     ; cl = 64 - s
	shl rdx, cl                 ; prev << (64 - s)
	shl r10, cl                 ; w0 << (64 - s)
	neg ecx                     ; cl = s
	shr rax, cl                 ; w0 >> s
	or rax, rdx                 ; out0
	mov rdx, r8                 ; prev = w1
	shr r8, cl                  ; w1 >> s
	or r8, r10                  ; out1
	mov [rsi], rax
	mov [rsi + 8], r8
	add rdi, 16
	add rsi, 16
	sub r9, 2
	jnz .pairs
	ARET rdi, rsi
.copy:                          ; s = 0: out = cur (read before written: R may alias A)
	mov rax, [rdi]
	mov [rsi], rax
	add rdi, 8
	add rsi, 8
	dec r9
	jnz .copy
	ARET rdi, rsi
.inPlace:                       ; from the low end: R[lead + k] = shrd(A[k], A[k-1], s), the lead
	sub r10, r9                 ; zeros last; every store lands above all words still to be read
	lea r11, [rsi + r10*8]      ; R[lead]
.inPlaceWords:
	cmp r9, 2
	jb .inPlaceTop
	mov rax, [rdi + r9*8 - 8]   ; A[k], k = r9 - 1
	mov rdx, [rdi + r9*8 - 16]  ; A[k-1]
	shrd rax, rdx, cl           ; s = 0: A[k]
	mov [r11 + r9*8 - 8], rax
	dec r9
	jmp .inPlaceWords
.inPlaceTop:                    ; k = 0: A[0] >> s
	mov rax, [rdi]
	shr rax, cl
	mov [r11], rax
	xor eax, eax
.inPlaceLead:
	test r10, r10
	jz .inPlaceDone
	sub r11, 8
	mov [r11], rax
	dec r10
	jmp .inPlaceLead
.inPlaceDone:
	ARET rdi, rsi
.allZero:
	xor eax, eax
.zero:
	mov [rsi], rax
	add rsi, 8
	dec r10
	jnz .zero
	ARET rdi, rsi