    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  The general add / sub (AArch64), the contracts of x64 Add.asm / Sub.asm:
//    uint64 Fullint64AddWIN(const uint64* A, const uint64* B, uint64* R, int lenA, int lenB, int lenR)
//         R = A + B over lenR words (the operands' missing top words 0; R shorter: its low
//         words); returns the carry out of R's top word (0 when R is longer than both)
//    int Fullint64SubWIN(const uint64* A, const uint64* B, uint64* R, int lenA, int lenB, int lenR)
//         R = |A - B| over lenR words; returns 1 (A > B), -1 (A < B, the operands swapped), 0
//         (equal: R zeroed). Leading zero words allowed.
//
//  The lengths resolved once, then R from its lowest word up in three runs (the carry in C
//  all through, the loops' control by sub / cbz / cbnz):
//    1) k1 = min(lenB, lenR) words of A op B (ADDSUB_LOOP: by one, two, then four words)
//    2) k2 = min(lenA, lenR) - k1 words of A op carry (the longer operand's tail)
//    3) k3 = lenR - min(lenA, lenR): add - the carry, then zeros; sub - zeros (no borrow is
//       left when |A| >= |B|)
//
//  Registers: x0 / x1 / x2 A / B / R (then past their low words), x3 / x4 / x5 lenA / lenB /
//  lenR (then k2 / k1 / k3), x6-x13 the words, x14 Sub's result.
// =============================================================================

// the runs 1) and 2); op adcs / sbcs, C set before
.macro RUNS op
    add x0, x0, x3, lsl #3              // past the low words
    add x1, x1, x4, lsl #3
    add x2, x2, x5, lsl #3
    cmp x4, x5
    csel x4, x4, x5, ls                 // k1 = min(lenB, lenR)
    cmp x3, x5
    csel x3, x3, x5, ls                 // min(lenA, lenR)
    sub x5, x5, x3                      // k3
    sub x3, x3, x4                      // k2
.endm

ENTRY Fullint64AddWIN
    sxtw x3, w3
    sxtw x4, w4
    sxtw x5, w5
    cmp x3, x4
    b.hs 1f
    mov x6, x0                          // A the longer (add commutes)
    mov x0, x1
    mov x1, x6
    mov x6, x3
    mov x3, x4
    mov x4, x6
1:  RUNS adcs
    cmn xzr, xzr                        // C = 0
    ADDSUB_LOOP adcs, x0, x1, x2, x4, x6, x7, x8, x9, x10, x11, x12, x13
    CARRY_RUN adcs, x0, x2, x3, x6
    cset x0, cs                         // the carry out of the operands
    cbz x5, 3f
    str x0, [x2, #-8]!                  // 3) the carry, then zeros
    mov x0, xzr
    subs x5, x5, #1
    b.eq 3f
2:  str xzr, [x2, #-8]!
    subs x5, x5, #1
    b.ne 2b
3:  ret
END Fullint64AddWIN

ENTRY Fullint64SubWIN
    sxtw x3, w3
    sxtw x4, w4
    sxtw x5, w5
1:  ldr x6, [x0]                        // A's leading zero words (one word kept)
    cbnz x6, 2f
    cmp x3, #1
    b.eq 2f
    add x0, x0, #8
    sub x3, x3, #1
    b 1b
2:  ldr x6, [x1]                        // B's
    cbnz x6, 3f
    cmp x4, #1
    b.eq 3f
    add x1, x1, #8
    sub x4, x4, #1
    b 2b
3:  mov x14, #1
    cmp x3, x4
    b.hi 7f
    b.lo 6f
    mov x6, xzr                         // equal lengths: the first differing word
4:  ldr x7, [x0, x6, lsl #3]
    ldr x8, [x1, x6, lsl #3]
    cmp x7, x8
    b.hi 7f
    b.lo 6f
    add x6, x6, #1
    cmp x6, x3
    b.ne 4b
5:  str xzr, [x2], #8                   // A == B: R = 0
    subs x5, x5, #1
    b.ne 5b
    mov w0, #0
    ret
6:  mov x6, x0                          // |A| < |B|: swapped
    mov x0, x1
    mov x1, x6
    mov x6, x3
    mov x3, x4
    mov x4, x6
    mov x14, #-1
7:  RUNS sbcs
    cmp xzr, xzr                        // C = 1: no borrow
    ADDSUB_LOOP sbcs, x0, x1, x2, x4, x6, x7, x8, x9, x10, x11, x12, x13
    CARRY_RUN sbcs, x0, x2, x3, x6
    cbz x5, 9f
8:  str xzr, [x2, #-8]!                 // 3) zeros
    subs x5, x5, #1
    b.ne 8b
9:  mov w0, w14
    ret
END Fullint64SubWIN
