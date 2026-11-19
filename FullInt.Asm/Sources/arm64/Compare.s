    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Magnitude compares (AArch64), the contracts of x64 Compare.asm / CompareN.asm:
//    int Fullint64CompareWIN(const uint64* A, const uint64* B, int lenA, int lenB)   -> -1, 0, 1
//         leading zero words allowed (skipped; one word kept)
//    int Fullint64CmpNWIN(const uint64* A, const uint64* B, int64 n)                 -> -1, 0, 1
//         equal lengths, n >= 1
//  From the top: one word if n is odd, then two a pass (ldp: the first differing word decides).
//
//  Registers: x0 / x1 A / B (moving up), x2 n (then the pairs), x4-x7 the words.
// =============================================================================

ENTRY Fullint64CompareWIN
    sxtw x2, w2
    sxtw x3, w3
1:  ldr x4, [x0]                        // A's leading zero words (one word kept)
    cbnz x4, 2f
    cmp x2, #1
    b.eq 2f
    add x0, x0, #8
    sub x2, x2, #1
    b 1b
2:  ldr x4, [x1]                        // B's
    cbnz x4, 3f
    cmp x3, #1
    b.eq 3f
    add x1, x1, #8
    sub x3, x3, #1
    b 2b
3:  cmp x2, x3
    b.ne 9f                             // the longer is the larger
    b fullint_arm64_cmpn
END Fullint64CompareWIN

ENTRY Fullint64CmpNWIN
fullint_arm64_cmpn:
    tbz x2, #0, 1f
    ldr x4, [x0], #8                    // an odd word first
    ldr x6, [x1], #8
    cmp x4, x6
    b.ne 9f
1:  lsr x2, x2, #1
    cbz x2, 3f
2:  ldp x4, x5, [x0], #16
    ldp x6, x7, [x1], #16
    cmp x4, x6
    b.ne 9f
    cmp x5, x7
    b.ne 9f
    sub x2, x2, #1
    cbnz x2, 2b
3:  mov w0, #0
    ret
9:  mov w0, #1                          // the flags of the deciding compare: lo -1, hi 1
    cneg w0, w0, lo
    ret
END Fullint64CmpNWIN
