    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Magnitude shifts (AArch64), the contracts of x64 Shift.asm. shift = ws * 64 + s (ws whole
//  words, s = 0..63 bits):
//    void Fullint64ShlWIN(A, R, int lenA, int lenR, int shift)    lenR >= lenA + ws + 1
//         R = [lead zeros][A shifted: lenA + 1 words][ws zeros]
//    void Fullint64ShrWIN(A, R, int lenA, int lenR, int shift)    lenR >= max(lenA - ws, 1)
//         R = [lead zeros][A[0 .. lenA - ws - 1] shifted right by s]; R may be A (with ws >= 1
//         and an overlap: from the low end)
//  From the top, two words a pass (ldp / stp post-increment); s = 0 a plain copy (a shift by
//  64 - 0 would be by 0 on AArch64: the register shifts take the count mod 64).
//
//  Registers: x0 A, x1 R (moving up), x2 lenA (then the words left), x3 lenR, x4 s, x5 ws,
//  x6 the lead, x7 the previous (upper) word, x8 = -s (64 - s mod 64), x9-x14 the words.
// =============================================================================

ENTRY Fullint64ShlWIN
    sxtw x2, w2
    sxtw x3, w3
    sxtw x4, w4
    lsr x5, x4, #6                      // ws
    and x4, x4, #63                     // s
    add x6, x2, x5
    add x6, x6, #1
    subs x6, x3, x6                     // lead = lenR - (lenA + 1 + ws)
    b.le 2f
1:  str xzr, [x1], #8
    subs x6, x6, #1
    b.ne 1b
2:  mov x7, xzr                         // the word above A's top: 0
    neg x8, x4
    cbz x4, 6f
    tbz x2, #0, 3f
    ldr x9, [x0], #8                    // an odd word first
    lsr x10, x9, x8                     // (prev << s = 0)
    str x10, [x1], #8
    mov x7, x9
3:  lsr x2, x2, #1
    cbz x2, 8f
4:  ldp x9, x10, [x0], #16              // two a pass: out = prev << s | cur >> (64 - s)
    lsl x11, x7, x4
    lsr x12, x9, x8
    orr x11, x11, x12
    lsl x12, x9, x4
    lsr x13, x10, x8
    orr x12, x12, x13
    stp x11, x12, [x1], #16
    mov x7, x10
    sub x2, x2, #1
    cbnz x2, 4b
    b 8f
6:  ldr x9, [x0], #8                    // s = 0: out = prev
    str x7, [x1], #8
    mov x7, x9
    subs x2, x2, #1
    b.ne 6b
8:  lsl x7, x7, x4                      // the lowest word
    str x7, [x1], #8
    cbz x5, 10f
9:  str xzr, [x1], #8                   // ws zero words
    subs x5, x5, #1
    b.ne 9b
10: ret
END Fullint64ShlWIN

ENTRY Fullint64ShrWIN
    sxtw x2, w2
    sxtw x3, w3
    sxtw x4, w4
    lsr x5, x4, #6                      // ws
    and x4, x4, #63                     // s
    subs x2, x2, x5                     // count = lenA - ws source words
    b.le 30f                            // all shifted out
    neg x8, x4
    // R over A with whole words shifted out: the top-down pass would overwrite words of A
    // before reading them - that case goes from the low end (20:)
    cbz x5, 1f
    add x9, x2, x5
    add x9, x0, x9, lsl #3              // A's end
    cmp x1, x9
    b.hs 1f
    add x9, x1, x3, lsl #3              // R's end
    cmp x0, x9
    b.lo 20f
1:  subs x6, x3, x2                     // lead = lenR - count
    b.le 3f
2:  str xzr, [x1], #8
    subs x6, x6, #1
    b.ne 2b
3:  mov x7, xzr                         // the word above: 0
    cbz x4, 8f
    tbz x2, #0, 4f
    ldr x9, [x0], #8                    // an odd word first
    lsr x10, x9, x4                     // (prev << (64 - s) = 0)
    str x10, [x1], #8
    mov x7, x9
4:  lsr x2, x2, #1
    cbz x2, 10f
5:  ldp x9, x10, [x0], #16              // two a pass (both read before written: R may be A)
    lsr x11, x9, x4                     // out = cur >> s | prev << (64 - s)
    lsl x12, x7, x8
    orr x11, x11, x12
    lsr x12, x10, x4
    lsl x13, x9, x8
    orr x12, x12, x13
    stp x11, x12, [x1], #16
    mov x7, x10
    sub x2, x2, #1
    cbnz x2, 5b
    ret
8:  ldr x9, [x0], #8                    // s = 0: a copy
    str x9, [x1], #8
    subs x2, x2, #1
    b.ne 8b
10: ret

20: sub x6, x3, x2                      // from the low end: lead = lenR - count,
    add x10, x1, x6, lsl #3             // x10 = &R[lead]; R[lead + k] = A[k] >> s | A[k-1] << (64 - s)
    cmp x4, #0
    csetm x11, ne                       // the upper word's part: none when s = 0
    sub x2, x2, #1                      // k
    cbz x2, 22f
21: add x12, x0, x2, lsl #3
    ldp x13, x9, [x12, #-8]             // A[k-1], A[k]
    lsr x9, x9, x4
    lsl x13, x13, x8
    and x13, x13, x11
    orr x9, x9, x13
    str x9, [x10, x2, lsl #3]
    subs x2, x2, #1
    b.ne 21b
22: ldr x9, [x0]                        // k = 0: A[0] >> s
    lsr x9, x9, x4
    str x9, [x10]
    cbz x6, 24f
23: str xzr, [x10, #-8]!                // the lead zeros, last
    subs x6, x6, #1
    b.ne 23b
24: ret

30: str xzr, [x1], #8                   // nothing left: R = 0
    subs x3, x3, #1
    b.ne 30b
    ret
END Fullint64ShrWIN
