; =============================================================================
;  x86 (32-bit) multiply with mulx / adcx / adox (BMI2 + ADX: Broadwell, Zen and later),
;  next to Mult.asm (the baseline, kept as is); picked at start-up when Fullint64CpuFeatures
;  (x86/Cpu.asm) reports both. cdecl. Same contract as Fullint64MultWIN:
;
;    void Fullint64MultWIN_adx(A, B, R, int lenA, int lenB, int lenR)
;    lenR >= lenA + lenB; R = A * B, the words above lenA + lenB zero
;
;  Operand scanning in 32-bit digits: R is cleared, then for every digit a of the shorter
;  operand (from the low end) R[i ..] += a * B, one row:
;    mulx hi, lo, b[j];  lo + previous hi (adcx: the CF chain) + R[i+j] (adox: the OF chain)
;  The two carry chains run side by side and never meet the loop control (lea / jecxz only),
;  the accumulators stay in registers. The baseline's column sums live on the stack, three
;  read-modify-write adc chains per word pair: ~2 cycles per 32x32 product (GMP ~1.2-1.9).
;
;  Digits: words are big-endian (word 0 = most significant), each word little-endian
;  (low dword at +0). Walking up from the low end a row goes word by word down in memory:
;  an even row (i even) puts B word w's two digits at R word (w + i/2) +0 / +4; an odd row
;  at +4 of that word and +0 of the next one (8 bytes lower).
;
;  Registers in a row: edx = a, esi -> B word, edi -> R word, ecx = B words left,
;  ebx / ebp = the previous high halves (alternating), eax = the product word in work.
; =============================================================================

section .text

%define L_ROWS  esp             ; digits of the outer operand left
%define L_AP    esp+4           ; outer operand: the current word (its address)
%define L_RP    esp+8           ; R word of the current row's first digit
%define L_ODD   esp+12          ; 0: the next row is even, 1: odd
%define L_BEND  esp+16          ; inner operand: its lowest word
%define L_BW    esp+20          ; inner operand: words
%define FRM     24
%define AR_A    esp+FRM+20
%define AR_B    esp+FRM+24
%define AR_R    esp+FRM+28
%define AR_LA   esp+FRM+32
%define AR_LB   esp+FRM+36
%define AR_LR   esp+FRM+40

; one B word (at esi + %1) of an even row: its digits into R word (edi + %1) +0 / +4
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

global Fullint64MultWIN_adx
Fullint64MultWIN_adx:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRM

	; R = 0 (lenR words)
	mov edi, [AR_R]
	mov ecx, [AR_LR]
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
	add ebx, ebx
	mov [L_ROWS], ebx           ; rows = its digits
	lea esi, [esi + ecx*8 - 8]  ; the inner operand's lowest word
	mov [L_BEND], esi
	mov [L_BW], ecx
	mov eax, [AR_R]
	mov edx, [AR_LR]
	lea eax, [eax + edx*8 - 8]  ; R's lowest word
	mov [L_RP], eax
	mov dword [L_ODD], 0

.row:
	mov eax, [L_AP]
	mov ebx, [L_ODD]
	mov edx, [eax + ebx*4]      ; a = the outer digit (low dword first)
	mov esi, [L_BEND]
	mov edi, [L_RP]
	mov ecx, [L_BW]
	test edx, edx
	jz .rowDone                 ; a = 0: R stays
	xor ebp, ebp                ; previous high = 0; CF = OF = 0
	test ebx, ebx
	jnz .oddWords

.evenWords:                     ; B word -> R word +0 / +4
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
	adcx ebp, ecx
	adox ebp, ecx
	mov [edi], ebp
	jmp .rowDone

.oddWords:                      ; B word -> R word +4 / the next word +0
	test ecx, 1
	jz .oddPairs
	ODD_WORD 0
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ecx, [ecx - 1]
	jecxz .oddTop
.oddPairs:
	ODD_WORD 0
	ODD_WORD -8
	lea esi, [esi - 16]
	lea edi, [edi - 16]
	lea ecx, [ecx - 2]
	jecxz .oddTop
	jmp .oddPairs
.oddTop:                        ; the row's top digit: R word (now edi) +4, still 0
	adcx ebp, ecx
	adox ebp, ecx
	mov [edi + 4], ebp

.rowDone:                       ; the next digit of a: the high dword, or the next word's low
	mov eax, [L_ODD]
	xor eax, 1
	mov [L_ODD], eax
	jnz .sameWord
	sub dword [L_AP], 8         ; both digits of this word done
	sub dword [L_RP], 8         ; R moves one word up with every two rows
.sameWord:
	dec dword [L_ROWS]
	jnz .row

	add esp, FRM
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
