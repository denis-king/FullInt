; =============================================================================
;  x86 (32-bit): the low half of a product, R = A·B mod β^n, with mulx / adcx / adox
;  (BMI2 + ADX), next to MultAdx.asm and in its 32-bit digits. cdecl:
;
;    void Fullint64MulLoWIN_adx(A, B, R, int lenA, int lenB, int n)
;    1 <= n <= lenA + lenB; R: n words (2n digits)
;
;  Rows as MultAdx.asm (R cleared, the shorter operand outside), but a row i (the outer digit
;  i from the low end) keeps only its digits below 2n. With "room" = n - floor(i/2) words:
;    even row: min(lenInner, room) inner words; the top digit when lenInner < room
;    odd row:  min(lenInner, room - 1) inner words, then the top digit when lenInner < room,
;              otherwise the low digit of the next inner word alone (at digit 2n - 1)
;  Everything else is dropped. The decision is made before the row (L_TOP): the carry chains
;  own the flags while the row runs. Rows: min(2·lenOuter, 2n).
;
;  Registers in a row: edx = a, esi -> inner word, edi -> R word, ecx = inner words left,
;  ebx / ebp = the previous high halves (alternating), eax = the product word in work.
; =============================================================================

section .text

%define L_ROWS  esp             ; rows left
%define L_AP    esp+4           ; outer operand: the current word (its address)
%define L_RP    esp+8           ; R word of the current row's first digit
%define L_ODD   esp+12          ; 0: the next row is even, 1: odd
%define L_BEND  esp+16          ; inner operand: its lowest word
%define L_BW    esp+20          ; inner operand: words
%define L_ROOM  esp+24          ; n - floor(i/2): R words from the row's first one
%define L_TOP   esp+28          ; 1: the row's top digit is below 2n
%define FRM     32
%define AR_A    esp+FRM+20
%define AR_B    esp+FRM+24
%define AR_R    esp+FRM+28
%define AR_LA   esp+FRM+32
%define AR_LB   esp+FRM+36
%define AR_N    esp+FRM+40

; one inner word (at esi + %1) of an even row: its digits into R word (edi + %1) +0 / +4
%macro EVEN_WORD 1
	mulx ebx, eax, [esi + %1]
	adcx eax, ebp
	adox eax, [edi + %1]
	mov [edi + %1], eax
	mulx ebp, eax, [esi + %1 + 4]
	adcx eax, ebx
	adox eax, [edi + %1 + 4]
	mov [edi + %1 + 4], eax
%endmacro

; the same for an odd row: R word (edi + %1) +4 and the next word +0
%macro ODD_WORD 1
	mulx ebx, eax, [esi + %1]
	adcx eax, ebp
	adox eax, [edi + %1 + 4]
	mov [edi + %1 + 4], eax
	mulx ebp, eax, [esi + %1 + 4]
	adcx eax, ebx
	adox eax, [edi + %1 - 8]
	mov [edi + %1 - 8], eax
%endmacro

global Fullint64MulLoWIN_adx
Fullint64MulLoWIN_adx:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRM

	; R = 0 (n words)
	mov edi, [AR_R]
	mov ecx, [AR_N]
	add ecx, ecx
	xor eax, eax
	rep stosd

	; the outer operand is the shorter one: fewer rows, longer inner loops
	mov eax, [AR_A]
	mov ebx, [AR_LA]
	mov esi, [AR_B]
	mov ecx, [AR_LB]
	cmp ebx, ecx
	jbe .order
	xchg eax, esi
	xchg ebx, ecx
.order:                         ; eax / ebx: outer operand and words, esi / ecx: inner
	lea eax, [eax + ebx*8 - 8]  ; the outer operand's lowest word
	mov [L_AP], eax
	lea esi, [esi + ecx*8 - 8]  ; the inner operand's lowest word
	mov [L_BEND], esi
	mov [L_BW], ecx
	mov edx, [AR_N]
	mov [L_ROOM], edx
	cmp ebx, edx
	cmova ebx, edx
	add ebx, ebx
	mov [L_ROWS], ebx           ; rows: 2·min(lenOuter, n) digits
	mov eax, [AR_R]
	lea eax, [eax + edx*8 - 8]  ; R's lowest word
	mov [L_RP], eax
	mov dword [L_ODD], 0

.row:
	mov eax, [L_AP]
	mov ebx, [L_ODD]
	mov edx, [eax + ebx*4]      ; a = the outer digit (low dword first)
	test edx, edx
	jz .rowDone                 ; a = 0: R stays
	mov esi, [L_BEND]
	mov edi, [L_RP]
	mov ecx, [L_ROOM]
	xor eax, eax
	cmp [L_BW], ecx
	setb al                     ; lenInner < room: the top digit fits
	mov [L_TOP], eax
	sub ecx, ebx                ; whole inner words that fit: room (even), room - 1 (odd)
	cmp ecx, [L_BW]
	cmova ecx, [L_BW]
	xor ebp, ebp                ; previous high = 0; CF = OF = 0
	test ebx, ebx
	jnz .oddWords

.evenWords:                     ; (ecx >= 1) inner word -> R word +0 / +4
	test ecx, 1                 ; (CF = OF = 0 still)
	jz .evenPairs
	EVEN_WORD 0                 ; an odd count: one word first
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ecx, [ecx - 1]
	jecxz .evenTop
.evenPairs:                     ; two words a pass
	EVEN_WORD 0
	EVEN_WORD -8
	lea esi, [esi - 16]
	lea edi, [edi - 16]
	lea ecx, [ecx - 2]
	jecxz .evenTop
	jmp .evenPairs
.evenTop:                       ; the row's top digit: R word (now edi) +0, still 0
	mov ecx, [L_TOP]
	jecxz .evenCut              ; the row ended at 2n: its carry is dropped
	mov ecx, 0
	adcx ebp, ecx
	adox ebp, ecx
	mov [edi], ebp
.evenCut:
	jmp .rowDone

.oddWords:                      ; inner word -> R word +4 / the next word +0
	test ecx, ecx               ; (flags are free: the chains have not started)
	jz .oddEnd                  ; the last row: no whole word fits
	test ecx, 1                 ; (CF = OF = 0)
	jz .oddPairs
	ODD_WORD 0
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ecx, [ecx - 1]
	jecxz .oddEnd
.oddPairs:
	ODD_WORD 0
	ODD_WORD -8
	lea esi, [esi - 16]
	lea edi, [edi - 16]
	lea ecx, [ecx - 2]
	jecxz .oddEnd
	jmp .oddPairs
.oddEnd:
	mov ecx, [L_TOP]
	jecxz .oddHalf
	mov ecx, 0                  ; the row's top digit: R word (now edi) +4, still 0
	adcx ebp, ecx
	adox ebp, ecx
	mov [edi + 4], ebp
	jmp .rowDone
.oddHalf:                       ; the next inner word's low digit alone, at digit 2n - 1
	mulx ebx, eax, [esi]
	adcx eax, ebp
	adox eax, [edi + 4]
	mov [edi + 4], eax

.rowDone:                       ; the next digit of a: the high dword, or the next word's low
	mov eax, [L_ODD]
	xor eax, 1
	mov [L_ODD], eax
	jnz .sameWord
	sub dword [L_AP], 8         ; both digits of this word done
	sub dword [L_RP], 8         ; R moves one word up with every two rows
	dec dword [L_ROOM]
.sameWord:
	dec dword [L_ROWS]
	jnz .row

	add esp, FRM
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
