; =============================================================================
;  abi86.inc - the 32-bit kernels on Windows and Linux from one source (included by every file)
;
;  Both use cdecl: the arguments on the stack, the result in eax (edx:eax), ebx / esi / edi / ebp
;  callee-saved - the bodies are the same. What differs is in the symbols:
;
;    win32 (COFF)   C names carry a leading underscore (_Fullint64AddWIN); calls between the
;                   kernels go straight to them.
;    elf32 (Linux)  no underscore. In a shared library a call to a global (preemptible) symbol
;                   goes through the PLT, and the i386 PLT needs ebx = the GOT - which these
;                   kernels never set. So every entry has a hidden alias (name_int, the same
;                   address, not exported) and the kernels call each other through it: a plain
;                   relative call, no PLT, no text relocation.
;
;  ENTRY name      an exported entry (global, the label; on ELF also name_int)
;  EXTERNK name    a kernel of another file that this one calls or jumps to
;  KREF(name)      the symbol to call / jump to for a kernel (in this file or another)
;
;  (No NASM --prefix _ in the builds any more: the underscore is put here.)
; =============================================================================

%ifndef FULLINT_ABI86_INC
%define FULLINT_ABI86_INC

%ifidn __OUTPUT_FORMAT__, win32
    %define FULLINT_WIN32 1
%endif

%macro ENTRY 1
    %ifdef FULLINT_WIN32
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
    %ifdef FULLINT_WIN32
        extern _%1
    %else
        extern %1 %+ _int
    %endif
%endmacro

%ifdef FULLINT_WIN32
    %define KREF(n) _ %+ n
%else
    %define KREF(n) n %+ _int
%endif

; "align n" in code: long NOPs (as abi.inc on x64)
%use smartalign
alignmode p6

%endif
