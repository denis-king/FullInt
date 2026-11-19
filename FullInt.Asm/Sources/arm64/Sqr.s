    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  R = A² (AArch64), the contract of x64 Sqr.asm:
//    void Fullint64SqrWIN(A, R, int lenA, int lenR)     lenR >= 2·lenA, R != A;
//    the words of R above 2·lenA are zero.
//
//  A² = 2·T + D (indices from the low end):
//    T = sum over i < j of a_i·a_j·β^(i+j): rows, row i adds a_i·(a_(i+1) .. a_(n-1)) at
//        r_(2i+1) (ADDMUL_1, arith.inc), the row's top word r_(i+n) is new (R cleared first);
//    R doubled: one adcs pass, two words a step;
//    D = sum a_i²·β^(2i) added: one pass, the carry between the steps in a register.
//
//  Registers: x0 A, x1 R, x2 n, x3 lenR, x4 a_i's address, x5 r_(2i)'s end, x6 the row's
//  length, x7 a_i, x8 / x9 / x10 the row's A / R pointers and count, x11 the carry, x12-x15
//  the words.
// =============================================================================

ENTRY Fullint64SqrWIN
    sxtw x2, w2
    sxtw x3, w3
    mov x8, x1                          // R cleared: the rows add into it
    mov x9, x3
1:  str xzr, [x8], #8
    subs x9, x9, #1
    b.ne 1b
    add x1, x1, x3, lsl #3              // past r_0
    add x4, x0, x2, lsl #3
    sub x4, x4, #8                      // &a_0
    mov x5, x1                          // past r_(2i): r_(2i+1) .. pre-decremented
    sub x6, x2, #1                      // row 0: n - 1 words
2:  cbz x6, 4f
    ldr x7, [x4]                        // a_i
    sub x5, x5, #8
    cbz x7, 3f                          // a zero word adds nothing (its top word stays 0)
    mov x8, x4
    mov x9, x5
    mov x10, x6
    ADDMUL_1 x8, x9, x10, x7, x11, x12, x13, x14, x15
    str x11, [x9, #-8]                  // r_(i+n)
3:  sub x4, x4, #8
    sub x5, x5, #8
    sub x6, x6, #1
    b 2b
4:  mov x8, x1                          // R = 2·T: the 2n words, one carry chain
    mov x10, x2
    cmn xzr, xzr
5:  ldp x12, x13, [x8, #-16]!
    adcs x13, x13, x13
    adcs x12, x12, x12
    stp x12, x13, [x8]
    sub x10, x10, #1
    cbnz x10, 5b
    add x4, x0, x2, lsl #3              // + D: a_k² into r_(2k), r_(2k+1)
    mov x8, x1
    mov x11, xzr                        // the carry (0 / 1)
6:  ldr x7, [x4, #-8]!
    mul x14, x7, x7
    umulh x15, x7, x7
    adds x14, x14, x11
    adc x15, x15, xzr                   // (no carry out: a_k² < β² - 2β + 2)
    ldp x12, x13, [x8, #-16]!           // r_(2k+1), r_(2k)
    adds x13, x13, x14
    adcs x12, x12, x15
    cset x11, cs
    stp x12, x13, [x8]
    subs x2, x2, #1
    b.ne 6b
    ret
END Fullint64SqrWIN

// =============================================================================
//  The low half of a product (AArch64), the contract of x64 MulLoAdx.asm:
//    void Fullint64MulLoWIN(A, B, R, int lenA, int lenB, int n)
//      R = A·B mod β^n, 1 <= n <= lenA + lenB; R: n words.
//  Rows (R cleared first): row j (b_j from the low end) adds A·b_j at r_j, only its words below
//  position n: min(lenA, n - j) of them; the row's top word r_(lenA+j) when it is below n,
//  otherwise its carry is dropped. Rows j < min(lenB, n).
//
//  Registers: x0 A's end, x1 b_j's end, x2 R's end (past r_0), x3 lenA, x4 rows left, x5 n - j,
//  x6 b_j, x7 / x8 / x9 the row's A / R pointers and count, x10 the carry, x11-x14 the words.
// =============================================================================
ENTRY Fullint64MulLoWIN
    sxtw x3, w3
    sxtw x4, w4
    sxtw x5, w5
    mov x8, x2                          // R = 0
    mov x9, x5
1:  str xzr, [x8], #8
    subs x9, x9, #1
    b.ne 1b
    add x0, x0, x3, lsl #3
    add x1, x1, x4, lsl #3
    add x2, x2, x5, lsl #3
    cmp x4, x5
    csel x4, x4, x5, ls                 // rows: min(lenB, n)
2:  ldr x6, [x1, #-8]!                  // b_j
    cbz x6, 4f
    mov x7, x0
    mov x8, x2
    cmp x3, x5
    csel x9, x3, x5, ls                 // min(lenA, n - j)
    ADDMUL_1 x7, x8, x9, x6, x10, x11, x12, x13, x14
    cmp x3, x5
    b.hs 4f                             // lenA >= n - j: the top word is at n or above
    str x10, [x8, #-8]                  // r_(lenA+j)
4:  sub x2, x2, #8
    sub x5, x5, #1
    subs x4, x4, #1
    b.ne 2b
    ret
END Fullint64MulLoWIN
