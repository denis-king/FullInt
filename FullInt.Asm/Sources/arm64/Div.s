    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Division (AArch64), the contracts of x64 Div.asm, Div2Long.asm and DivRecip.asm. AArch64 has
//  no 128 / 64 division (udiv is 64 / 64), so every kernel here divides by a precomputed
//  reciprocal of the normalized divisor (Moller, Granlund: "Improved division by invariant
//  integers", IEEE Trans. Computers 2011): the reciprocal once a division (DIVLU, arith.inc:
//  32-bit digits by udiv), then a multiplication or two per quotient word. The baseline entries
//  (Div2longWin, DivWin) and the _recip ones the engine picks with Cpu::Recip run the same
//  arithmetic; they differ by their contracts (the window in R / a work buffer).
//
//  Limbs big-endian (index 0 = most significant word).
// =============================================================================

// =============================================================================
//  void Fullint64Div2longWin(A, d, Q, u64* r, int lenA)        (and Fullint64Div2longWIN_recip)
//  d != 0, lenA >= 1; Q = A / d (lenA words), *r = A mod d; Q may be A. Returns 0 in x0.
//  d normalized (shifted left by sh until its top bit is set), A shifted by the same sh on the
//  fly, the remainder shifted back at the end; per word STEP_2BY1.
//
//  Registers: x0 A (moving up), x1 d, x2 Q, x3 r, x4 words left, x5 sh, x6 = -sh, x7 the mask,
//  x8 v, x9 the remainder (u1), x10 u0, x11 the next word, x12 the quotient word, x13-x15 and
//  x9-x12 scratch for the reciprocal.
// =============================================================================
ENTRY Fullint64Div2longWin
fullint_arm64_div2long:
    sxtw x4, w4
    clz x5, x1                          // sh
    lsl x1, x1, x5                      // d normalized
    neg x6, x5
    cmp x5, #0
    csetm x7, ne
    RECIP x8, x1, x9, x10, x11, x12, x13, x14, x15, x16, x17
    ldr x11, [x0], #8                   // A[0]
    lsr x9, x11, x6                     // the bits shifted out of A[0] start the remainder (< d)
    and x9, x9, x7
    subs x4, x4, #1
    b.eq 2f
1:  ldr x13, [x0], #8                   // A[i+1]
    lsl x10, x11, x5                    // A[i] with the top bits of A[i+1]
    lsr x14, x13, x6
    and x14, x14, x7
    orr x10, x10, x14
    mov x11, x13
    STEP_2BY1 x12, x9, x10, x1, x8, x14, x15
    str x12, [x2], #8
    subs x4, x4, #1
    b.ne 1b
2:  lsl x10, x11, x5                    // the last word
    STEP_2BY1 x12, x9, x10, x1, x8, x14, x15
    str x12, [x2]
    lsr x9, x9, x5                      // the remainder, not normalized
    str x9, [x3]
    mov x0, xzr
    ret
END Fullint64Div2longWin

ENTRY Fullint64Div2longWIN_recip
    b fullint_arm64_div2long
END Fullint64Div2longWIN_recip

// =============================================================================
//  The long division (Knuth D) with the 3-by-2 estimate (the paper's Algorithm 5): one quotient
//  word per step on a window W of lenB + 1 words (W < B·β):
//    q^ from the top three words of W and the top two of B, both normalized by sh (SHLD on
//    the fly: B and W stay as they are); q^ is the exact quotient of those words, the true
//    word q^ or (rarely) q^ - 1. (n2, n1) == (d1, d0) - the only case the 3-by-2 cannot take -
//    gives q^ = β - 1.
//    W -= q^·B (one word a pass, the carry in a register; q^ = 0 skips it); negative: q^ - 1,
//    W += B (until the carry comes out of the top).
//  v = floor((β^3 - 1) / (d1·β + d0)) - β: RECIP of d1, then RECIP_3BY2 for d0.
//
//  Registers (all the long divisions): x1 B, x2 Q (moving up), x4 steps left, x5 n = lenB,
//  x6 W (its top word), x7 d1, x8 d0, x9 v, x10 sh, x11 = -sh, x12 the mask, x13-x15 and
//  x19-x24 scratch (x19-x24 saved at the entry). x0 / x3 the kernel's own (A, R).
// =============================================================================

// d1, d0, v, sh / -sh / mask from B (x1, n = x5 >= 2)
.macro DIV_SETUP
    ldp x13, x14, [x1]
    mov x15, xzr
    cmp x5, #2
    b.eq 1f
    ldr x15, [x1, #16]
1:  clz x10, x13                        // sh
    neg x11, x10
    cmp x10, #0
    csetm x12, ne
    SHLD x7, x13, x14, x10, x11, x12, x19    // d1
    SHLD x8, x14, x15, x10, x11, x12, x19    // d0
    RECIP x9, x7, x13, x14, x15, x19, x20, x21, x22, x23, x24
    RECIP_3BY2 x9, x7, x8, x13, x14, x15
.endm

// one step on the window at x6: the quotient word to [x2], x2 advanced. appr = 1: DivQ's
// approximate quotient (the pass on the top min(lenB, steps left + 2) words of B only; a
// window above B's top words, or a nonzero top word after the pass, jumps to sat).
.macro DIV_STEP appr, sat
    ldp x13, x14, [x6]                  // n2, n1, n0: the top three words of W normalized
    ldr x15, [x6, #16]
    mov x19, xzr
    cmp x5, #2
    b.eq 11f                            // W has three words: nothing below them
    ldr x19, [x6, #24]
11: SHLD x20, x13, x14, x10, x11, x12, x23
    SHLD x21, x14, x15, x10, x11, x12, x23
    SHLD x22, x15, x19, x10, x11, x12, x23
.if \appr
    cmp x20, x7
    b.lo 13f
    b.hi \sat                           // (n2, n1) > (d1, d0): the left-out error only
    cmp x21, x8
    b.eq 12f                            // (n2, n1) == (d1, d0)
    b.hi \sat
    b 13f
.else
    cmp x20, x7
    b.ne 13f
    cmp x21, x8
    b.ne 13f
.endif
12: mov x14, #-1                        // q^ = β - 1
    b 15f
13: mul x13, x9, x20                    // 3-by-2: (n2 n1 n0) / (d1 d0)
    umulh x14, x9, x20
    adds x13, x13, x21
    adc x14, x14, x20                   // (q : q0) = v·n2 + (n2 : n1)
    msub x21, x7, x14, x21              // r1 = n1 - d1·q
    subs x22, x22, x8
    sbc x21, x21, x7                    // (r1 : r0) = (r1 : n0) - (d1 : d0)
    mul x15, x8, x14
    umulh x19, x8, x14                  // d0·q
    subs x22, x22, x15
    sbc x21, x21, x19
    add x14, x14, #1
    cmp x21, x13
    csetm x23, hs                       // r1 >= q0: q - 1, (r1 : r0) + (d1 : d0)
    add x14, x14, x23
    and x15, x23, x8
    and x19, x23, x7
    adds x22, x22, x15
    adc x21, x21, x19
    cmp x21, x7                         // (r1 : r0) >= (d1 : d0) (rare): q + 1
    b.lo 15f
    b.hi 14f
    cmp x22, x8
    b.lo 15f
14: add x14, x14, #1
15: cbz x14, 19f                        // q^ = 0: W stays
.if \appr
    add x15, x4, #2                     // the words of B kept: min(lenB, steps left + 2)
    cmp x15, x5
    csel x15, x15, x5, lo
.else
    mov x15, x5
.endif
    mov x24, x15
    add x19, x1, x15, lsl #3            // past B's kept words
    add x20, x6, x15, lsl #3
    add x20, x20, #8                    // past W's word against B's lowest one
    mov x21, xzr                        // the carry
16: ldr x22, [x19, #-8]!                // W -= q^·B, from the low end
    ldr x23, [x20, #-8]!
    mul x13, x22, x14
    umulh x15, x22, x14
    adds x13, x13, x21
    adc x15, x15, xzr
    subs x23, x23, x13
    cinc x21, x15, lo                   // + the borrow
    str x23, [x20]
    subs x24, x24, #1
    b.ne 16b
    ldr x23, [x6]
    subs x23, x23, x21
    str x23, [x6]
    b.lo 17f                            // a borrow out of the top: W < 0
.if \appr
    cbnz x23, \sat                      // the window reached B (the left-out error)
.endif
    b 19f
17: sub x14, x14, #1                    // W < 0: q^ - 1, W += B
.if \appr
    add x15, x4, #2                     // the same words of B as the pass
    cmp x15, x5
    csel x15, x15, x5, lo
.else
    mov x15, x5
.endif
    add x19, x1, x15, lsl #3
    add x20, x6, x15, lsl #3
    add x20, x20, #8
    cmn xzr, xzr                        // C = 0
18: ldr x22, [x19, #-8]!
    ldr x23, [x20, #-8]!
    adcs x23, x23, x22
    str x23, [x20]
    sub x15, x15, #1
    cbnz x15, 18b
    ldr x23, [x6]
    adcs x23, x23, xzr
    str x23, [x6]
    b.cc 17b                            // no carry out of the top: still negative
19: str x14, [x2], #8
.endm

.macro SAVE_X19_X24
    stp x19, x20, [sp, #-48]!
    stp x21, x22, [sp, #16]
    stp x23, x24, [sp, #32]
.endm

.macro RESTORE_X19_X24
    ldp x21, x22, [sp, #16]
    ldp x23, x24, [sp, #32]
    ldp x19, x20, [sp], #48
.endm

// =============================================================================
//  uint64 Fullint64DivWin(A, B, Q, R, int lenA, int lenB)
//  B without leading zero words. R: lenB + 2 words - R[0] = R[1] = 0, the remainder right-
//  aligned in R[2 ..]; Q: max(lenA - lenB + 1, 1) words (lenA < lenB: Q[0] = 0, R = A). R is
//  cleared here. Q may be A. Returns 0.
//  lenB = 1: Div2long into R[2]. Otherwise the long division on the window W = R[1 .. lenB + 1]
//  (W = [0, A's top lenB words] first): after a step W[0] = 0, the window moves one word up and
//  the next word of A comes in at its bottom.
//
//  Registers: as the long division; x0 A's next word, x3 R.
// =============================================================================
ENTRY Fullint64DivWin
    sxtw x4, w4
    sxtw x5, w5
    add x6, x5, #2                      // R = 0
    mov x7, x3
20: str xzr, [x7], #8
    subs x6, x6, #1
    b.ne 20b
    cmp x4, x5
    b.hs 22f
    add x7, x3, x5, lsl #3              // lenA < lenB: A right-aligned in R, Q[0] = 0
    add x7, x7, #16                     // (A copied first: Q may be A)
    add x8, x0, x4, lsl #3
21: ldr x9, [x8, #-8]!
    str x9, [x7, #-8]!
    subs x4, x4, #1
    b.ne 21b
    str xzr, [x2]
    mov x0, xzr
    ret
22: cmp x5, #1
    b.ne 23f
    ldr x1, [x1]                        // one word: Div2long, the remainder to R[2]
    add x3, x3, #16
    b fullint_arm64_div2long
23: SAVE_X19_X24
    DIV_SETUP
    add x6, x3, #8                      // W = R[1 .. n + 1] = [0, A[0 .. n-1]]
    add x13, x3, #16
    mov x14, x5
24: ldr x15, [x0], #8
    str x15, [x13], #8
    subs x14, x14, #1
    b.ne 24b
    sub x4, x4, x5
    add x4, x4, #1                      // steps = lenA - lenB + 1
25: DIV_STEP 0, 0
    subs x4, x4, #1
    b.eq 27f
    mov x13, x6                         // the window one word up (W[0] = 0 after a step),
    mov x14, x5                         // the next word of A in at its bottom
26: ldr x15, [x13, #8]
    str x15, [x13], #8
    subs x14, x14, #1
    b.ne 26b
    ldr x15, [x0], #8
    str x15, [x13]
    b 25b
27: RESTORE_X19_X24
    mov x0, xzr
    ret
END Fullint64DivWin

// =============================================================================
//  void Fullint64DivWIN_recip(A, B, Q, R, int lenA, int lenB, u64* work)
//  lenA >= lenB >= 2, B without leading zero words. Q: lenA - lenB + 1 words; R: lenB + 2 words
//  as DivWin leaves it (R[0] = R[1] = 0, the remainder in R[2 ..]); work: lenA + 1 words.
//  U = [0, A] in work, the window W = U[j .. j + lenB] moving down by a pointer (no copying).
//
//  u64 Fullint64DivQWIN_recip(A, B, Q, R, int lenA, int lenB, u64* work)
//  The quotient alone, approximately (x64 DivRecip.asm has the analysis): Q' ~ floor(A·β / B)
//  within 1, lenA - lenB + 2 words (the quotient and a guard word below it), R unused, work:
//  lenA + 2 words, U = [0, A, 0], lenB >= 3; the pass of a step on the top min(lenB, steps
//  left + 2) words of B only. Returns 0; or 1 - saturated (the rest of Q' β - 1): the caller
//  divides exactly.
// =============================================================================
.macro DIV_LONG appr
    sxtw x4, w4
    sxtw x5, w5
    SAVE_X19_X24
    mov x13, x6                         // U = [0, A (, 0)]
    str xzr, [x13], #8
    mov x14, x4
30: ldr x15, [x0], #8
    str x15, [x13], #8
    subs x14, x14, #1
    b.ne 30b
    sub x4, x4, x5
    add x4, x4, #1                      // steps = lenA - lenB + 1
.if \appr
    str xzr, [x13]
    add x4, x4, #1                      // one more: the guard word
.endif
    DIV_SETUP
31: DIV_STEP \appr, 35f
    add x6, x6, #8                      // the next window: one word down
    subs x4, x4, #1
    b.ne 31b
.if \appr
    mov x0, xzr                         // 0: Q' within 1
34: RESTORE_X19_X24
    ret
35: mov x13, #-1                        // saturated: the rest of Q' (this word on) β - 1
36: str x13, [x2], #8
    subs x4, x4, #1
    b.ne 36b
    mov x0, #1
    b 34b
.else
    stp xzr, xzr, [x3], #16             // the remainder: the last window's low lenB words
32: ldr x15, [x6], #8                   // (x6 = its W[1]; its top word is 0) -> R[2 ..]
    str x15, [x3], #8
    subs x5, x5, #1
    b.ne 32b
    RESTORE_X19_X24
    mov x0, xzr
    ret
.endif
.endm

ENTRY Fullint64DivWIN_recip
    DIV_LONG 0
END Fullint64DivWIN_recip

ENTRY Fullint64DivQWIN_recip
    DIV_LONG 1
END Fullint64DivQWIN_recip
