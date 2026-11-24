    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Schoolbook multiplication (AArch64), the contract of x64 Mult.asm:
//    uint64 Fullint64MultWIN(const uint64* A, const uint64* B, uint64* R, int lenA, int lenB, int lenR)
//         R = A * B, lenR >= lenA + lenB (the words above the product zeroed; a shorter R - the
//         product's low words - goes to MulLo, Sqr.s); returns 0
//  Row by row (operand scanning): the longer operand A times one word of B a row, from B's low
//  end. The first row is mul_1 (R = A * b + carry), the others addmul_1 (R += A * b); each row's
//  top word is the carry, a word no row before has written.
//
//  mul_1 / addmul_1: the macros of arith.inc (Sqr and MulLo take them too).
//
//  Registers: x0 A, x1 B, x2 R, x3 lenA, x4 rows left, x8 the row's end in R, x9 B's next word
//  (from below), x10 the B word, x11 / x12 / x13 the row's A / R pointers and count, x14 the
//  carry; the rows' scratch x5-x7, x15-x17, x19-x24 (saved at the entry).
// =============================================================================

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
    SAVE_X19_X24
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
    MUL_1 x11, x12, x13, x10, x14, x5, x6, x7, x15, x16, x17, x19, x20, x21, x22, x23, x24
    str x14, [x12, #-8]             // the row's top word
    subs x4, x4, #1
    b.eq 14f
13: sub x8, x8, #8                  // the next row: one word up
    ldr x10, [x9, #-8]!
    add x11, x0, x3, lsl #3
    mov x12, x8
    mov x13, x3
    ADDMUL_1 x11, x12, x13, x10, x14, x5, x6, x7, x15, x16, x17, x19, x20, x21, x22, x23, x24
    str x14, [x12, #-8]
    subs x4, x4, #1
    b.ne 13b
14: RESTORE_X19_X24
    mov x0, xzr
    ret
15: stp x29, x30, [sp, #-16]!          // R shorter than the product: its low words, MulLo (Sqr.s:
    mov x29, sp                         // the same arguments; A / B possibly swapped - the
    CALL Fullint64MulLoWIN              // product is the same), then 0
    ldp x29, x30, [sp], #16
    mov x0, xzr
    ret
END Fullint64MultWIN
