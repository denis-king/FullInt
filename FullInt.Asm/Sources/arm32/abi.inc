// =============================================================================
//  ARMv7-A (AArch32): Linux armhf (ELF). GNU assembler syntax, directives only (no C
//  preprocessor): the same file builds with gcc / clang through cc and with llvm-mc
//  (-triple=armv7-linux-gnueabihf).
//
//  The kernels are in ARM state (.arm): umaal and the conditional instructions without IT
//  blocks; the callers may be Thumb (the C++ library, gcc's default on armhf) - bl / blx to a
//  %function symbol switches state, the return is bx lr / pop {pc} (interworking on ARMv7).
//
//  The calling convention (AAPCS, hard-float - no float arguments here): arguments r0-r3, then
//  the stack; a 64-bit argument takes an even / odd register pair (r0:r1 or r2:r3) or an
//  8-byte-aligned stack slot (an int64_t after three pointers: the stack, r3 left unused). A
//  64-bit result in r0 (low) : r1 (high). r0-r3, r12 free; r4-r11, sp, lr kept for the caller
//  (lr free once saved).
//
//  Limbs are 64-bit words, big-endian (index 0 = the most significant word), as on x64 / x86 /
//  AArch64; each word little-endian in memory: its low 32 bits at the lower address. The
//  kernels work in 32-bit digits: word i holds digits (lo at +0, hi at +4); the loops run from
//  the high address (the low end of the number) down.
// =============================================================================

    .syntax unified
    .arch armv7-a
    .arm
    // the hard-float ABI (armhf): without it the objects are marked soft-float and the armhf
    // loader refuses the .so (no float arguments here - only the marking)
    .eabi_attribute 28, 1       // Tag_ABI_VFP_args: VFP registers

.macro ENTRY name
    .globl \name
    .type \name, %function
    .p2align 4
\name:
.endm

.macro END name
    .size \name, . - \name
.endm

    .section .note.GNU-stack, "", %progbits   // no executable stack
    .text
