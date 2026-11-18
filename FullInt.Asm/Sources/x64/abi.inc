; =============================================================================
;  abi.inc - the two x64 calling conventions in one place (included by every kernel)
;
;  Every kernel is one source assembled for both: nasm -f win64 (Windows) and -f elf64 /
;  macho64 (Linux / macOS, System V). There are no wrappers: each exported name is one entry
;  that takes its arguments in the platform's convention and goes straight into the body.
;  The exported names and signatures are the same on every platform (the ...WIN names are
;  historical: they mean the argument order C# and C++ use, not an ABI).
;
;                   Win64                               System V
;  arguments 1-4    rcx, rdx, r8, r9                    rdi, rsi, rdx, rcx
;  arguments 5-6    stack [rsp+28h], [rsp+30h]          r8, r9
;  arguments 7+     stack [rsp+38h] ...                 stack [rsp+8] ...
;  callee-saved     rbx rbp rdi rsi r12-r15 xmm6-15     rbx rbp r12-r15
;
;  Two kinds of bodies:
;
;  System V bodies (Add, Sub, Compare, Logic, Shift, Mult, MultAdx, Div, Div2Long,
;  ToDecString, FromDecString): the entry saves the callee-saved registers the body uses
;  (SAVE: rdi / rsi only on Win64, where they are callee-saved), moves the arguments to the
;  body's registers, and every return of the body is ARET (the same list restored).
;
;  Win64 bodies (the rest): on Win64 nothing changes. On System V the entry (WIN64_ARGS)
;  builds what a Win64 body expects - arguments 1-4 in rcx, rdx, r8, r9 and a Win64 frame on
;  the stack (the 32-byte home area, arguments 5+ above it) - and every return is WRET, which
;  on System V also drops that frame (ret 64). The bodies themselves are untouched.
; =============================================================================

%ifndef FULLINT_ABI_INC
%define FULLINT_ABI_INC

%ifidn __OUTPUT_FORMAT__, win64
    %define FULLINT_WIN64 1
%endif
%ifidn __OUTPUT_FORMAT__, macho64
    %define FULLINT_MACHO 1             ; macOS: System V, C names with a leading underscore
%endif
; The code section of every kernel file: 64-byte aligned, and every file ends on a 64-byte boundary
; (align 64 at its end), so each kernel's place in the 64-byte lines depends only on its own file.
; On Windows the name .text$zz: the linker merges .text$xx into .text sorted by the suffix, so the
; kernels go after the compiler's code (.text$mn) - a change in a kernel does not move the C++
; code (it did: the kernels were first in the image, so a change in their size moved all of
; C++ and slowed or sped up paths that never call them).
%macro CODE_SECTION 0
    %ifdef FULLINT_WIN64
        section .text$zz code align=64
    %else
        section .text align=64
    %endif
%endmacro


; "align n" in code fills with long NOPs (one or two instructions, not a run of 1-byte nops): hot
; loops are aligned explicitly, so their place in the 32 / 64-byte lines does not depend on the
; size of whatever is linked before them (it moved with every entry change: 10-35% on Zen 3)
%use smartalign
alignmode p6

; ---- exported entries -------------------------------------------------------
; ENTRY name     an exported entry. ELF (Linux): also name_int, a hidden alias at the same address
;                (not exported) - the kernels jump to each other through it: a jump to a global,
;                preemptible symbol is not allowed in a shared library (R_X86_64_PC32 "recompile
;                with -fPIC"). Mach-O: the C name has a leading underscore.
; EXTERNK name   a kernel of another file this one jumps to; KREF(name) the symbol to jump to
%macro ENTRY 1
    %ifdef FULLINT_WIN64
        global %1
%1:
    %elifdef FULLINT_MACHO
        global _%1
_%1:
    %else
        global %1:function
        global %1 %+ _int:function hidden
%1:
%1 %+ _int:
    %endif
%endmacro

%macro EXTERNK 1
    %ifdef FULLINT_WIN64
        extern %1
    %elifdef FULLINT_MACHO
        extern _%1
    %else
        extern %1 %+ _int
    %endif
%endmacro

%ifdef FULLINT_WIN64
    %define KREF(n) n
%elifdef FULLINT_MACHO
    %define KREF(n) _ %+ n
%else
    %define KREF(n) n %+ _int
%endif

; ---- saving callee-saved registers (System V bodies) ----------------------------------
; SAVE r1, r2, ...: pushes them in order - rdi / rsi only on Win64 (volatile in System V);
; FI_NPUSH counts the pushes (for STACKARG). RESTORE with the same list pops them back.
%assign FI_NPUSH 0

%macro SAVE 0-*
    %assign FI_NPUSH 0
    %rep %0
        %ifdef FULLINT_WIN64
            push %1
            %assign FI_NPUSH FI_NPUSH + 1
        %elifidni %1, rsi
        %elifidni %1, rdi
        %else
            push %1
            %assign FI_NPUSH FI_NPUSH + 1
        %endif
        %rotate 1
    %endrep
%endmacro

%macro RESTORE 0-*
    %rep %0
        %rotate -1
        %ifdef FULLINT_WIN64
            pop %1
        %elifidni %1, rsi
        %elifidni %1, rdi
        %else
            pop %1
        %endif
    %endrep
%endmacro

; ARET r1, r2, ...: a return of a System V body - RESTORE with the entry's list, then ret
%macro ARET 0-*
    RESTORE %{1:-1}
    ret
%endmacro

; STACKARG(k, n): the memory operand of argument k passed on the stack, n words pushed since
; the entry (Win64: arguments 5+, System V: 7+)
%ifdef FULLINT_WIN64
    %define STACKARG(k, n) [rsp + 8 + 8 * ((k) - 1) + 8 * (n)]
%else
    %define STACKARG(k, n) [rsp + 8 + 8 * ((k) - 7) + 8 * (n)]
%endif

; ---- Win64 bodies on System V ------------------------------------------------------
; WIN64_ARGS: the first lines of an entry whose body is written for Win64 (rax, r10, r11
; clobbered). Win64: nothing. System V: a 64-byte Win64 frame below the return address -
; [rsp] the return address, [rsp+8 .. +40) the home area, [rsp+40] argument 5, [rsp+48]
; argument 6, [rsp+56] argument 7 (copied from the caller's stack) - and arguments 1-4 moved
; to rcx, rdx, r8, r9. 64 bytes: up to 7 arguments, and rsp keeps its alignment (16 | 64).
%define FI_WFRAME 64

%macro WIN64_ARGS 0
    %ifndef FULLINT_WIN64
        mov rax, [rsp]                  ; the return address
        mov r10, [rsp + 8]              ; argument 7 (if any: the caller's stack otherwise)
        sub rsp, FI_WFRAME
        mov [rsp], rax
        mov [rsp + 40], r8              ; argument 5
        mov [rsp + 48], r9              ; argument 6
        mov [rsp + 56], r10             ; argument 7
        mov r8, rdx                     ; argument 3
        mov r9, rcx                     ; argument 4
        mov rcx, rdi                    ; argument 1
        mov rdx, rsi                    ; argument 2
    %endif
%endmacro

; WRET: a return of a Win64 body (System V: the frame of WIN64_ARGS goes too)
%macro WRET 0
    %ifdef FULLINT_WIN64
        ret
    %else
        ret FI_WFRAME
    %endif
%endmacro

%endif
