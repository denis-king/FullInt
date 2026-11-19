    .include "abi.inc"

// =============================================================================
//  Schoolbook multiplication (AArch64), the contract of x64 Mult.asm:
//    uint64 Fullint64MultWIN(const uint64* A, const uint64* B, uint64* R, int lenA, int lenB, int lenR)
//         R = A * B, lenR >= lenA + lenB (the words above the product zeroed; a shorter R - the
//         product's low words - goes to the portable C++ kernel); returns 0
//  Row by row (operand scanning): the longer operand A times one word of B a row, from B's low
//  end. The first row is mul_1 (R = A * b + carry), the others addmul_1 (R += A * b); each row's
//  top word is the carry, a word no row before has written.
//
//  mul_1 / addmul_1 as macros (the Div and Sqr kernels to come take them too). A word product:
//  mul (the low half) and umulh (the high half) - AArch64 has no 64 x 64 -> 128 in one
//  instruction. The carry adds by adds / adc within the step; the loop's subs comes after it.
//
//  Registers: x0 A, x1 B, x2 R, x3 lenA, x4 rows left, x5 the R word (addmul_1), x6 / x7 the
//  product's halves, x8 the row's end in R, x9 B's next word (from below), x10 the B word,
//  x11 / x12 / x13 the row's A / R pointers and count, x14 the carry, x15 the A word.
// =============================================================================

// rp[-1 .. -n] = ap[-1 .. -n] * b; ap / rp move down n words (rp at the last word written),
// n -> 0, c the carry out (the row's next word up)
.macro MUL_1 ap, rp, n, b, c, t, lo, hi
    mov \c, xzr
1:  ldr \t, [\ap, #-8]!
    mul \lo, \t, \b
    umulh \hi, \t, \b
    adds \lo, \lo, \c
    adc \c, \hi, xzr
    str \lo, [\rp, #-8]!
    subs \n, \n, #1
    b.ne 1b
.endm

// rp[-1 .. -n] += ap[-1 .. -n] * b; the same moves, c the carry out
.macro ADDMUL_1 ap, rp, n, b, c, t, lo, hi, w
    mov \c, xzr
1:  ldr \t, [\ap, #-8]!
    ldr \w, [\rp, #-8]!
    mul \lo, \t, \b
    umulh \hi, \t, \b
    adds \lo, \lo, \c
    adc \hi, \hi, xzr
    adds \lo, \lo, \w
    adc \c, \hi, xzr
    str \lo, [\rp]
    subs \n, \n, #1
    b.ne 1b
.endm

ENTRY Fullint64MultWIN
    sxtw x3, w3                     // lenA
    sxtw x4, w4                     // lenB
    sxtw x5, w5                     // lenR
    cmp x3, x4
    b.hs 10f
    mov x6, x0                      // A the longer
    mov x0, x1
    mov x1, x6
    mov x6, x3
    mov x3, x4
    mov x4, x6
10: sub x6, x5, x3
    subs x6, x6, x4                 // the words above the product
    b.lt 15f
    mov x7, x2
    b.eq 12f
11: str xzr, [x7], #8
    subs x6, x6, #1
    b.ne 11b
12: add x8, x2, x5, lsl #3          // the first row's end: R's end
    add x9, x1, x4, lsl #3          // past B's lowest word
    ldr x10, [x9, #-8]!
    add x11, x0, x3, lsl #3
    mov x12, x8
    mov x13, x3
    MUL_1 x11, x12, x13, x10, x14, x15, x6, x7
    str x14, [x12, #-8]             // the row's top word
    subs x4, x4, #1
    b.eq 14f
13: sub x8, x8, #8                  // the next row: one word up
    ldr x10, [x9, #-8]!
    add x11, x0, x3, lsl #3
    mov x12, x8
    mov x13, x3
    ADDMUL_1 x11, x12, x13, x10, x14, x15, x6, x7, x5
    str x14, [x12, #-8]
    subs x4, x4, #1
    b.ne 13b
14: mov x0, xzr
    ret
15: TAILCALL Fullint64MultWIN_generic   // R shorter than the product (the arguments as they came,
                                        // A / B possibly swapped: the product is the same)
END Fullint64MultWIN
