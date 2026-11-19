    .include "abi.inc"

// =============================================================================
//  Equal-length add / sub (AArch64), the contracts of x64 AddSubN.asm:
//    uint64 Fullint64AddNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
//         R = A + B (n words, n >= 1); returns the carry out of the top word
//    uint64 Fullint64SubNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
//         R = A - B mod beta^n; returns the borrow (1 when A < B)
//  R may be A or B (a group's words are all read before any is written).
//
//  The carry lives in the C flag through the whole loop: adcs / sbcs only, the loop control by
//  sub / tbz / cbz / cbnz, which leave the flags alone. AArch64's C on a subtraction is NOT
//  borrow: C = 1 means no borrow, so the subtraction starts with C = 1 (cmp xzr, xzr) and the
//  borrow returned is !C.
//  From the low end (the high address) down: one word if n is odd, two if n & 2, then groups
//  of four with ldp / stp pre-decrement.
//
//  Registers: x0 / x1 / x2 past the low words of A / B / R, x3 n (then the groups), x4-x11 the words.
// =============================================================================

.macro ADDSUBN op, sub, out
    add x0, x0, x3, lsl #3
    add x1, x1, x3, lsl #3
    add x2, x2, x3, lsl #3
.if \sub
    cmp xzr, xzr                    // C = 1: no borrow
.else
    cmn xzr, xzr                    // C = 0: no carry
.endif
    tbz x3, #0, 1f
    ldr x4, [x0, #-8]!              // one word
    ldr x5, [x1, #-8]!
    \op x4, x4, x5
    str x4, [x2, #-8]!
1:  tbz x3, #1, 2f
    ldp x4, x6, [x0, #-16]!         // two words: x4 the upper, x6 the lower
    ldp x5, x7, [x1, #-16]!
    \op x6, x6, x7
    \op x4, x4, x5
    stp x4, x6, [x2, #-16]!
2:  lsr x3, x3, #2
    cbz x3, 4f
3:  ldp x4, x6, [x0, #-16]          // four words
    ldp x5, x7, [x1, #-16]
    ldp x8, x10, [x0, #-32]!
    ldp x9, x11, [x1, #-32]!
    \op x6, x6, x7
    \op x4, x4, x5
    \op x10, x10, x11
    \op x8, x8, x9
    stp x4, x6, [x2, #-16]
    stp x8, x10, [x2, #-32]!
    sub x3, x3, #1
    cbnz x3, 3b
4:  cset x0, \out
    ret
.endm

ENTRY Fullint64AddNWIN
    ADDSUBN adcs, 0, cs                 // the carry: C
END Fullint64AddNWIN

ENTRY Fullint64SubNWIN
    ADDSUBN sbcs, 1, cc                 // the borrow: !C
END Fullint64SubNWIN
