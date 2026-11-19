    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Bitwise AND / OR / XOR with BigInteger semantics (two's complement view of negative
//  numbers) on sign-magnitude operands (AArch64), the contract of x64 Logic.asm:
//    int Fullint64AndWIN(A, B, R, int lenA, int lenB, int lenR, int signs)   (Or, Xor the same)
//  A, B magnitudes (lenA / lenB >= 1 words); signs bit 0 = A negative, bit 1 = B negative;
//  R lenR >= max(lenA, lenB) + 1 words, the result's magnitude; returns 1 when it is negative.
//
//  One pass from the low word up; every word goes through t = (word ^ mask) + carry, mask 0 or
//  all ones, carry starting at mask & 1 - two's complement on the fly (nothing for a
//  non-negative operand); the result back to a magnitude the same way, maskR = op(maskA, maskB).
//  Both operands non-negative (the common case): op on the words alone, two a pass.
//
//  Registers: x0 / x1 / x2 past the low words of A / B / R, x3 / x4 / x5 the words left of
//  A / B / R, x6 signs; x7 / x9 / x12 carryA / carryB / carryR, x8 / x10 / x11 maskA / maskB /
//  maskR, x13 / x14 the words.
// =============================================================================

.macro BITWISE name, op
ENTRY Fullint64\name\()WIN
    sxtw x3, w3
    sxtw x4, w4
    sxtw x5, w5
    add x0, x0, x3, lsl #3
    add x1, x1, x4, lsl #3
    add x2, x2, x5, lsl #3
    tst w6, #3
    b.ne 10f
    cmp x3, x4                          // non-negative: A the longer (the ops commute)
    b.hs 1f
    mov x7, x0
    mov x0, x1
    mov x1, x7
    mov x7, x3
    mov x3, x4
    mov x4, x7
1:  sub x3, x3, x4                      // A's words above B
    sub x5, x5, x4
    sub x5, x5, x3                      // R's words above A
    tbz x4, #0, 2f
    ldr x13, [x0, #-8]!                 // one word
    ldr x14, [x1, #-8]!
    \op x13, x13, x14
    str x13, [x2, #-8]!
2:  lsr x4, x4, #1
    cbz x4, 4f
3:  ldp x13, x7, [x0, #-16]!            // two a pass
    ldp x14, x8, [x1, #-16]!
    \op x13, x13, x14
    \op x7, x7, x8
    stp x13, x7, [x2, #-16]!
    sub x4, x4, #1
    cbnz x4, 3b
4:  cbz x3, 6f                          // A's words above B: op with 0
5:  ldr x13, [x0, #-8]!
    \op x13, x13, xzr
    str x13, [x2, #-8]!
    sub x3, x3, #1
    cbnz x3, 5b
6:  cbz x5, 8f                          // the rest: zeros
7:  str xzr, [x2, #-8]!
    sub x5, x5, #1
    cbnz x5, 7b
8:  mov w0, #0
    ret

10: and x7, x6, #1                      // carryA = signA
    neg x8, x7                          // maskA
    ubfx x9, x6, #1, #1                 // carryB
    neg x10, x9                         // maskB
    \op x11, x8, x10                    // maskR
    and x12, x11, #1                    // carryR
11: mov x13, xzr                        // A's word (0 above its top)
    cbz x3, 12f
    ldr x13, [x0, #-8]!
    sub x3, x3, #1
12: eor x13, x13, x8
    adds x13, x13, x7
    cset x7, cs
    mov x14, xzr                        // B's word
    cbz x4, 13f
    ldr x14, [x1, #-8]!
    sub x4, x4, #1
13: eor x14, x14, x10
    adds x14, x14, x9
    cset x9, cs
    \op x13, x13, x14
    eor x13, x13, x11                   // the result back to a magnitude
    adds x13, x13, x12
    cset x12, cs
    str x13, [x2, #-8]!
    subs x5, x5, #1
    b.ne 11b
    and w0, w11, #1
    ret
END Fullint64\name\()WIN
.endm

    BITWISE And, and
    BITWISE Or, orr
    BITWISE Xor, eor
