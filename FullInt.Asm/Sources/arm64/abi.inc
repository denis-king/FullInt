// =============================================================================
//  AArch64: one source for Linux / Android (ELF), macOS / iOS (Mach-O) and Windows on ARM (COFF).
//  GNU assembler syntax with directives only (.macro / .ifdef, no C preprocessor): the same file
//  builds with clang, GNU as and llvm-mc (Tools/llvm). The object format by --defsym:
//    FULLINT_MACHO=1   Mach-O: C symbols carry a leading underscore
//    FULLINT_COFF=1    COFF (Windows on ARM)
//    neither           ELF
//
//  The calling convention (AAPCS64) is the same on the three: arguments x0-x7 (an int argument:
//  the low 32 bits, w0-w7, the upper half undefined - sxtw / uxtw before use as a length), the
//  result in x0; x0-x15 free (x16 / x17 too, but kept free here: the linker's veneers use them
//  between functions), x19-x28, x29 (FP), x30 (LR) and the low halves of v8-v15 kept for the
//  caller. x18 is the platform register (Apple, Windows): never touched.
//  Limbs big-endian, as on x86 (index 0 = the most significant word): the loops run from the
//  high address down, ldp / stp with pre-decrement.
// =============================================================================

.ifdef FULLINT_MACHO
    .macro ENTRY name
        .globl _\name
        .p2align 6
    _\name:
    .endm
    .macro END name
    .endm
.else
.ifdef FULLINT_COFF
    .macro ENTRY name
        .globl \name
        .def \name
        .scl 2
        .type 32
        .endef
        .p2align 6
    \name:
    .endm
    .macro END name
    .endm
.else
    .macro ENTRY name
        .globl \name
        .type \name, %function
        .p2align 6
    \name:
    .endm
    .macro END name
        .size \name, . - \name
    .endm
    .section .note.GNU-stack, "", %progbits   // no executable stack
.endif
.endif

// a jump to another C function (a tail call: its return is ours)
.macro TAILCALL name
.ifdef FULLINT_MACHO
    b _\name
.else
    b \name
.endif
.endm

// a call to another entry (the caller saves x29 / x30 around it)
.macro CALL name
.ifdef FULLINT_MACHO
    bl _\name
.else
    bl \name
.endif
.endm

    .text
