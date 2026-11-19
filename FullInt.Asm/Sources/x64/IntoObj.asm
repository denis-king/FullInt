default rel
%include "abi.inc"
CODE_SECTION

; =============================================================================
;  x64: the C++ into-operations' common case on the number objects themselves (C++ only:
;  FullInt64's layout, checked by a static_assert in FullInt64Engine.cpp). The whole common case
;  in one call - the checks, the words' addresses, the loop, the result's header - and 0;
;  anything else returns 1 with nothing written, and the caller takes the C++ path.
;
;  FullInt64 (x64):  +0   inline words (8) / the heap pointer (when cap > 8)
;                    +40h cap   +44h off   +48h size   +4Ch neg (byte)
;  A number's words: base + off*8, size of them, index 0 = the most significant word.
;
;    int Fullint64AndObjWIN(FullInt64* r, const FullInt64* a, const FullInt64* b)
;      r = a & b when both are non-negative, the shorter has at most 32 words, r is neither and has
;      room for them: eight words a pass with SSE2 (every x86-64), then four, two, one; the leading
;      zero words taken off. (The C++ loop: 4 words 1.17 of GMP - its vectorized path's checks.)
;
;    int Fullint64ShlObjWIN(FullInt64* r, const FullInt64* a, int shift)
;      r = a << shift when r is not a, a is not zero and has 1-32 words, shift > 0 and not a
;      multiple of 64, r has room (la + shift/64 + 1 words): r[i] = a[i-1] << b | a[i] >> (64 - b),
;      b = shift % 64, with SSE2's 64-bit lane shifts (psllq / psrlq), two words a pass, then
;      shift/64 zero words; only r[0] can be zero. (The C++ paths: 8 words 1.07 of GMP; 1-32
;      words here 0.64-0.72.) Not this
;      case: 1 (the C# DLL); in the C++ library on Windows (FULLINT_CPP) a jump on to its general
;      path with the arguments as they came - the caller jumps here and has no frame of its own
;      (a call with a check after it gave every length a frame: 1 word 1.25x).
;
;  Registers (Win64 volatile only): r11 r, rdx / r8 / r9 the words of a / b / r, r10 n,
;  rcx index, rax; xmm0-5.
; =============================================================================

%define OBJ_CAP  40h
%define OBJ_OFF  44h
%define OBJ_SIZE 48h
%define OBJ_NEG  4Ch
%define OBJ_INLINE 8

ENTRY Fullint64AndObjWIN
	WIN64_ARGS
	cmp rcx, rdx
	je .no
	cmp rcx, r8
	je .no
	mov al, [rdx + OBJ_NEG]
	or al, [r8 + OBJ_NEG]
	jnz .no
	mov r9d, [rdx + OBJ_SIZE]     ; la
	mov r10d, [r8 + OBJ_SIZE]     ; lb
	cmp r9d, r10d
	jae .ordered
	xchg rdx, r8                  ; a the longer
	xchg r9d, r10d
.ordered:                         ; the result: n = lb words (a's low lb words & b's)
	cmp r10d, 32
	ja .no                        ; longer: the C++ loop (64-1024 words 6-21% faster than this one)
	cmp r10d, [rcx + OBJ_CAP]
	ja .no
	mov r11, rcx                  ; r
	sub r9d, r10d                 ; la - lb
	add r9d, [rdx + OBJ_OFF]
	mov rax, [rdx]                ; the heap pointer or the first inline word (read either way)
	cmp dword [rdx + OBJ_CAP], OBJ_INLINE
	cmovbe rax, rdx               ; inline: the object itself
	lea rdx, [rax + r9*8]         ; a's low lb words
	mov r9d, [r8 + OBJ_OFF]
	mov rax, [r8]
	cmp dword [r8 + OBJ_CAP], OBJ_INLINE
	cmovbe rax, r8
	lea r8, [rax + r9*8]          ; b's words
	mov r9, [r11]
	cmp dword [r11 + OBJ_CAP], OBJ_INLINE
	cmovbe r9, r11                ; r's words from its base
	mov [r11 + OBJ_SIZE], r10d    ; n (the leading zeros taken off below)
	xor ecx, ecx                  ; the word index
	lea rax, [r10 - 8]
	cmp rcx, rax
	jg .quad
	align 16
.oct:                             ; eight words: four 16-byte pand
	movdqu xmm0, [rdx + rcx*8]
	movdqu xmm1, [rdx + rcx*8 + 16]
	movdqu xmm2, [rdx + rcx*8 + 32]
	movdqu xmm3, [rdx + rcx*8 + 48]
	movdqu xmm4, [r8 + rcx*8]
	movdqu xmm5, [r8 + rcx*8 + 16]
	pand xmm0, xmm4
	pand xmm1, xmm5
	movdqu xmm4, [r8 + rcx*8 + 32]
	movdqu xmm5, [r8 + rcx*8 + 48]
	pand xmm2, xmm4
	pand xmm3, xmm5
	movdqu [r9 + rcx*8], xmm0
	movdqu [r9 + rcx*8 + 16], xmm1
	movdqu [r9 + rcx*8 + 32], xmm2
	movdqu [r9 + rcx*8 + 48], xmm3
	add rcx, 8
	cmp rcx, rax
	jle .oct
.quad:
	lea rax, [r10 - 4]
	cmp rcx, rax
	jg .pair
	movdqu xmm0, [rdx + rcx*8]    ; four words
	movdqu xmm1, [rdx + rcx*8 + 16]
	movdqu xmm4, [r8 + rcx*8]
	movdqu xmm5, [r8 + rcx*8 + 16]
	pand xmm0, xmm4
	pand xmm1, xmm5
	movdqu [r9 + rcx*8], xmm0
	movdqu [r9 + rcx*8 + 16], xmm1
	add rcx, 4
.pair:
	lea rax, [r10 - 2]
	cmp rcx, rax
	jg .single
	movdqu xmm0, [rdx + rcx*8]    ; two words
	movdqu xmm4, [r8 + rcx*8]
	pand xmm0, xmm4
	movdqu [r9 + rcx*8], xmm0
	add rcx, 2
.single:
	cmp rcx, r10
	je .lead
	mov rax, [rdx + rcx*8]
	and rax, [r8 + rcx*8]
	mov [r9 + rcx*8], rax
.lead:                            ; the leading zero words (one word stays)
	xor ecx, ecx
	dec r10
	jz .header
.zeros:
	cmp qword [r9 + rcx*8], 0
	jne .header
	inc rcx
	cmp rcx, r10
	jne .zeros
.header:
	mov [r11 + OBJ_OFF], ecx
	sub [r11 + OBJ_SIZE], ecx
	mov byte [r11 + OBJ_NEG], 0
	xor eax, eax
	WRET
.no:
	mov eax, 1
	WRET

%ifdef FULLINT_CPP
%ifdef FULLINT_WIN64
	extern Fullint64ShlIntoGeneral
%endif
%endif

ENTRY Fullint64ShlObjWIN
	WIN64_ARGS
	cmp rcx, rdx                  ; (rcx r, rdx a, r8 shift kept until every check has passed)
	je .shlNo
	test r8d, r8d
	jle .shlNo                    ; 0 or negative
	test r8d, 63
	jz .shlNo                     ; whole words: a copy (the C++ path)
	mov r10d, [rdx + OBJ_SIZE]    ; la
	lea eax, [r10 - 1]
	cmp eax, 31
	ja .shlNo                     ; 1-32 words only
	mov eax, r8d
	shr eax, 6
	lea eax, [rax + r10 + 1]      ; n
	cmp eax, [rcx + OBJ_CAP]
	ja .shlNo
	mov r11, rcx                  ; r
	mov r9d, [rdx + OBJ_OFF]
	mov rcx, [rdx]
	cmp dword [rdx + OBJ_CAP], OBJ_INLINE
	cmovbe rcx, rdx
	lea rcx, [rcx + r9*8]         ; a's words from the top
	cmp r10d, 1
	jne .shlGo
	cmp qword [rcx], 0
	jne .shlGo
	mov rcx, r11                  ; zero: the C++ path with the arguments as they came
	jmp .shlNo
.shlGo:
	mov [r11 + OBJ_SIZE], eax     ; n (a leading zero word taken off below)
	movzx eax, byte [rdx + OBJ_NEG]
	mov [r11 + OBJ_NEG], al
	mov r9, [r11]
	cmp dword [r11 + OBJ_CAP], OBJ_INLINE
	cmovbe r9, r11                ; r's words from the top
	mov eax, r8d
	and eax, 63                   ; b
	movd xmm4, eax
	neg eax
	add eax, 64
	movd xmm5, eax                ; 64 - b
	movq xmm0, [rcx]              ; r[0] = a[0] >> (64 - b)
	psrlq xmm0, xmm5
	movq [r9], xmm0
	add r9, 8
	lea rax, [r10 - 1]            ; r[1 .. la-1]: a[i-1] << b | a[i] >> (64 - b), rcx at a[i-1]
	cmp rax, 2
	jb .shlSingle
.shlPair:                         ; two words: 16-byte psllq / psrlq / por
	movdqu xmm0, [rcx]
	movdqu xmm1, [rcx + 8]
	psllq xmm0, xmm4
	psrlq xmm1, xmm5
	por xmm0, xmm1
	movdqu [r9], xmm0
	add rcx, 16
	add r9, 16
	sub rax, 2
	cmp rax, 2
	jae .shlPair
.shlSingle:
	test rax, rax
	jz .shlLast
	movq xmm0, [rcx]
	movq xmm1, [rcx + 8]
	psllq xmm0, xmm4
	psrlq xmm1, xmm5
	por xmm0, xmm1
	movq [r9], xmm0
	add rcx, 8
	add r9, 8
.shlLast:                         ; r[la] = a[la-1] << b
	movq xmm0, [rcx]
	psllq xmm0, xmm4
	movq [r9], xmm0
	mov eax, r8d                  ; shift / 64 zero words below r[la]
	shr eax, 6
	jz .shlLead
.shlZero:
	mov qword [r9 + 8], 0
	add r9, 8
	dec eax
	jnz .shlZero
.shlLead:                         ; r[0] zero: one word off (a's top bits all went to r[1])
	mov r9, [r11]
	cmp dword [r11 + OBJ_CAP], OBJ_INLINE
	cmovbe r9, r11
	xor eax, eax
	cmp qword [r9], 0
	sete al
	mov [r11 + OBJ_OFF], eax
	sub [r11 + OBJ_SIZE], eax
	xor eax, eax
	WRET
.shlNo:                           ; not this case: the arguments untouched
%ifdef FULLINT_CPP
%ifdef FULLINT_WIN64
	jmp Fullint64ShlIntoGeneral   ; the C++ library: on to its general path (a jump, its return is ours)
%endif
%endif
	mov eax, 1
	WRET

; the file ends on a 64-byte boundary (the section align=64): the code linked after it keeps
; its alignment whatever changes here
	align 64, int3
