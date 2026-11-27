// =============================================================================
//  ARMv7, the C++ library only (FullInt.Cpp, FULLINT_ARM32_ASM): the entries without an ARMv7
//  kernel yet, each a jump to the portable C++ one (src/kernels_generic.cpp builds them as
//  ..._generic when the asm is on). The .NET library's .so is the asm kernels alone
//  (build-native.sh leaves this file out; its core takes the managed paths for the rest).
//  Thumb: the C++ code is Thumb (gcc's armhf default), b.w reaches it without a state change.
// =============================================================================

    .syntax unified
    .arch armv7-a
    .thumb
    .eabi_attribute 28, 1       // Tag_ABI_VFP_args: the hard-float ABI (armhf), as abi.inc
    .section .note.GNU-stack, "", %progbits
    .text

.macro GENERIC name
    .globl \name
    .type \name, %function
    .thumb_func
    .p2align 2
\name:
    b.w \name\()_generic
    .size \name, . - \name
.endm

    GENERIC Fullint64AddWIN
    GENERIC Fullint64AddNHdrWIN
    GENERIC Fullint64SubCmpNHdrWIN
    GENERIC Fullint64SubWIN
    GENERIC Fullint64ToDecFracWIN
    GENERIC Fullint64SqrWIN
    GENERIC Fullint64MulLoWIN
    GENERIC Fullint64DivWin
    GENERIC Fullint64DivExactWIN
    GENERIC Fullint64CpuFeatures
    GENERIC Fullint64DivQWIN_recip
