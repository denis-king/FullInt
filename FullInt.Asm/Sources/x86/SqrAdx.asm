; =============================================================================
;  x86 (32-bit) square with mulx / adcx / adox (BMI2 + ADX), next to MultAdx.asm; picked at
;  start-up when Fullint64CpuFeatures (x86/Cpu.asm) reports both (without them the callers
;  multiply a·a). cdecl:
;
;    void Fullint64SqrWIN_adx(A, R, int lenA, int lenR)     lenR >= 2·lenA, R != A;
;    R = A², the words above 2·lenA zero
;
;  In 32-bit digits d_0 .. d_(2n-1) from the low end: A² = 2·T + D,
;    T = sum over p < q of d_p·d_q·2^(32(p+q))  (half the digit products),  D = sum d_p²·2^(64p)
;  T by rows as MultAdx (R cleared, row p adds d_p·(d_(p+1) ..), its top digit is new):
;    p = 2i + 1 (the high digit of word i): the digits from word i + 1 on, word-aligned, into
;      R digit 4i + 3 on (odd: ODD_WORD);
;    p = 2i (the low digit of word i): first d_2i·d_(2i+1) alone (the two halves of one word)
;      into R digit 4i + 1, then the words from i + 1 on into R digit 4i + 2 on (EVEN_WORD).
;  Then one pass: each R word p doubled on the CF chain, d_p² added on the OF chain.
;  Loop control by lea / jecxz only; the parity of a row's length is tested before its first
;  adcx (test sets ZF, which mulx / adcx / adox leave alone).
;
;  Words big-endian (word 0 = most significant), each word little-endian (low dword at +0).
;  Registers in a row: edx = d_p, esi -> A word, edi -> R word, ecx = A words left,
;  ebx / ebp = the previous high halves (alternating), eax = the product word in work.
; =============================================================================

section .text

%define L_LEFT  esp             ; A words from the current one up (n - i)
%define L_AP    esp+4           ; A word i (its address)
%define L_RP    esp+8           ; R word 2i
%define FRM     12
%define AR_A    esp+FRM+20
%define AR_R    esp+FRM+24
%define AR_LA   esp+FRM+28
%define AR_LR   esp+FRM+32

; one A word (at esi + %1) of an even-aligned run: its digits into R word (edi + %1) +0 / +4
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

; the same for an odd-aligned run: R word (edi + %1) +4 and the next word +0
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

global Fullint64SqrWIN_adx
Fullint64SqrWIN_adx:
	push ebp
	push ebx
	push esi
	push edi
	sub esp, FRM

	; R = 0 (lenR words; a store loop: rep stosd's start-up outweighs a short square, as in
	; x64/SqrAdx.asm)
	mov edi, [AR_R]
	mov ecx, [AR_LR]
	xor eax, eax
.clear:
	mov [edi], eax
	mov [edi + 4], eax
	lea edi, [edi + 8]
	dec ecx
	jnz .clear

	mov eax, [AR_A]
	mov ecx, [AR_LA]
	mov [L_LEFT], ecx
	lea eax, [eax + ecx*8 - 8]  ; A word 0 (the lowest)
	mov [L_AP], eax
	mov eax, [AR_R]
	mov edx, [AR_LR]
	lea eax, [eax + edx*8 - 8]  ; R word 0
	mov [L_RP], eax

.word:                          ; the two rows of A word i
	; row p = 2i: d_2i·d_(2i+1) into R word 2i +4, then words i+1 .. into R word 2i+1 on
	mov eax, [L_AP]
	mov edx, [eax]              ; d_2i
	test edx, edx
	jz .oddRow                  ; a zero digit adds nothing
	mov edi, [L_RP]
	xor ebp, ebp                ; previous high = 0
	mov ecx, [L_LEFT]
	dec ecx                     ; words above word i
	test ecx, 1                 ; ZF: an even count; CF = OF = 0
	mulx ebx, eax, [eax + 4]    ; d_2i·d_(2i+1)
	adcx eax, ebp
	adox eax, [edi + 4]
	mov [edi + 4], eax
	mov ebp, ebx
	mov esi, [L_AP]
	lea esi, [esi - 8]          ; A word i+1
	lea edi, [edi - 8]          ; R word 2i+1
	jecxz .toEvenTop            ; (jecxz is short only: through a jmp, the flags stay)
	jz .evenPairs
	jmp .evenOne
.toEvenTop:
	jmp .evenTop
.evenOne:
	EVEN_WORD 0                 ; an odd count: one word first
	lea esi, [esi - 8]
	lea edi, [edi - 8]
	lea ecx, [ecx - 1]
	jecxz .evenTop
.evenPairs:
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

.oddRow:                        ; row p = 2i+1: words i+1 .. into R word 2i+1 +4 on
	mov ecx, [L_LEFT]
	dec ecx
	jz .next                    ; the top word: nothing above d_(2n-1)
	mov eax, [L_AP]
	mov edx, [eax + 4]          ; d_(2i+1)
	test edx, edx
	jz .next
	lea esi, [eax - 8]          ; A word i+1
	mov edi, [L_RP]
	lea edi, [edi - 8]          ; R word 2i+1
	xor ebp, ebp
	test ecx, 1                 ; CF = OF = 0
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

.next:
	sub dword [L_AP], 8
	sub dword [L_RP], 16
	dec dword [L_LEFT]
	jnz .word

	; R = 2·T + D: R word p doubled (CF chain), d_p² added (OF chain)
	mov esi, [AR_A]
	mov ecx, [AR_LA]
	lea esi, [esi + ecx*8 - 8]  ; A word 0
	mov edi, [AR_R]
	mov eax, [AR_LR]
	lea edi, [edi + eax*8 - 8]  ; R word 0
	xor eax, eax                ; CF = OF = 0
.pair:                          ; A word i: its digits' squares into R words 2i, 2i+1
	jecxz .done
	mov edx, [esi]
	mulx ebx, eax, edx          ; d_2i²
	mov ebp, [edi]
	adcx ebp, ebp
	adox ebp, eax
	mov [edi], ebp
	mov ebp, [edi + 4]
	adcx ebp, ebp
	adox ebp, ebx
	mov [edi + 4], ebp
	mov edx, [esi + 4]
	mulx ebx, eax, edx          ; d_(2i+1)²
	mov ebp, [edi - 8]
	adcx ebp, ebp
	adox ebp, eax
	mov [edi - 8], ebp
	mov ebp, [edi - 4]
	adcx ebp, ebp
	adox ebp, ebx
	mov [edi - 4], ebp
	lea esi, [esi - 8]
	lea edi, [edi - 16]
	lea ecx, [ecx - 1]
	jmp .pair

.done:
	add esp, FRM
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
