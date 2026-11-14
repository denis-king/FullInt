
; =============================================================================
;  x86 (32-bit) port of x64/Sub.asm: the same zero skip, compare, swap and the
;  same three runs as Add, with sbb. cdecl, arguments on the stack.
;
;  int Fullint64SubWIN(A, B, R, int lenA, int lenB, int lenR)
;    R = |A - B| (lenR words), returns sign(A - B): 1, -1 or 0
;
;  Register map (x64 -> x86)
;    rdi A, rsi B, rcx R        -> esi, edi, ebp
;    rdx lenA, r8 lenB          -> edx, ecx (then min/k values as in Add)
;    rbx compare index          -> ebx
;    r15 result sign            -> stack [esp+12] after .init
;    k2 / k3 / -k2              -> stack [esp+4] / [esp+8] / [esp]
;  A limb is compared high dword first, then low dword.
; =============================================================================

section .text

global Fullint64SubWIN
Fullint64SubWIN:
	push ebp
	push ebx
	push esi
	push edi
	mov esi, [esp+20]             ; A
	mov edi, [esp+24]             ; B
	mov ebp, [esp+28]             ; R
	mov edx, [esp+32]             ; lenA
	mov ecx, [esp+36]             ; lenB

.skipZerosA:
	mov eax, [esi]
	or eax, [esi + 4]
	jnz .skipZerosB
	cmp edx, 1
	je .skipZerosB
	add esi, 8
	dec edx
	jmp .skipZerosA

.skipZerosB:
	mov eax, [edi]
	or eax, [edi + 4]
	jnz .compare
	cmp ecx, 1
	je .compare
	add edi, 8
	dec ecx
	jmp .skipZerosB

.compare:
	cmp edx, ecx
	ja .greater
	jb .swap

	xor ebx, ebx
.comparationLoop:
	mov eax, [esi + ebx*8 + 4]    ; high dword
	cmp eax, [edi + ebx*8 + 4]
	ja .greater
	jb .swap
	mov eax, [esi + ebx*8]        ; low dword
	cmp eax, [edi + ebx*8]
	ja .greater
	jb .swap
	inc ebx
	cmp ebx, edx
	jne .comparationLoop

	;we have equal A and B
	mov ecx, [esp+40]             ; lenR
	xor eax, eax
.zeroResultLoop:
	mov [ebp], eax
	mov [ebp + 4], eax
	add ebp, 8
	dec ecx
	jnz .zeroResultLoop
	jmp .return                   ; eax = 0

.swap:
	xchg esi, edi
	xchg edx, ecx
	mov eax, -1
	jmp .init
.greater:
	mov eax, 1
.init:
	push eax                      ; result sign
	mov ebx, [esp+44]             ; lenR (40 + 4 for the push)

	; here |A| >= |B| and lenA >= lenB (leading zeros skipped). Lengths are resolved once,
	; so the loops have no per-word range checks. R is walked from its lowest word up:
	;   1) k1 = min(lenB, lenR) words of A - B - borrow   (borrow stays in CF: dec/lea/mov keep it)
	;   2) k2 = min(lenA, lenR) - k1 words of A - borrow
	;   3) k3 = lenR - min(lenA, lenR) words of zeros     (no borrow is left when |A| >= |B|)
	lea esi, [esi + edx*8]        ; end of A (past its lowest word)
	lea edi, [edi + ecx*8]        ; end of B
	lea ebp, [ebp + ebx*8]        ; end of R
	cmp ecx, ebx
	cmova ecx, ebx                ; k1 = min(lenB, lenR)
	cmp edx, ebx
	cmova edx, ebx                ; min(lenA, lenR)
	sub ebx, edx                  ; k3
	sub edx, ecx                  ; k2
	push ebx                      ; [esp+8] k3
	push edx                      ; [esp+4] k2
	neg edx
	push edx                      ; [esp]   -k2
	lea eax, [ecx*8]
	sub esi, eax                  ; bases = top word of run 1, index ecx runs k1-1 .. 0 (low word first)
	sub edi, eax
	sub ebp, eax
	dec ecx
	clc

.subPairs:                        ; 1) A - B
	mov eax, [esi + ecx*8]
	sbb eax, [edi + ecx*8]
	mov [ebp + ecx*8], eax
	mov eax, [esi + ecx*8 + 4]
	sbb eax, [edi + ecx*8 + 4]
	mov [ebp + ecx*8 + 4], eax
	dec ecx
	jns .subPairs

	mov ecx, [esp+4]              ; k2 (tested by jecxz, which keeps CF)
	jecxz .tail
	mov edx, [esp]                ; -k2
	lea esi, [esi + edx*8]        ; bases = top word of run 2
	lea ebp, [ebp + edx*8]
	lea ecx, [ecx - 1]
.subBorrow:                       ; 2) A - borrow
	mov eax, [esi + ecx*8]
	sbb eax, 0
	mov [ebp + ecx*8], eax
	mov eax, [esi + ecx*8 + 4]
	sbb eax, 0
	mov [ebp + ecx*8 + 4], eax
	dec ecx
	jns .subBorrow

.tail:
	mov ebx, [esp+8]              ; k3
	test ebx, ebx
	jz .done
	xor eax, eax
.zeros:                           ; 3) zeros
	sub ebp, 8
	mov [ebp], eax
	mov [ebp + 4], eax
	dec ebx
	jnz .zeros

.done:
	add esp, 12
	pop eax                       ; result sign
.return:
	pop edi
	pop esi
	pop ebx
	pop ebp
	ret
