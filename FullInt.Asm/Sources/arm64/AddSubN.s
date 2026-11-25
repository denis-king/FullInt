    .include "abi.inc"
    .include "arith.inc"

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
//  of four with ldp / stp pre-decrement (ADDSUB_LOOP, arith.inc).
//
//  Registers: x0 / x1 / x2 past the low words of A / B / R, x3 n (then the groups), x4-x11 the words.
// =============================================================================

ENTRY Fullint64AddNWIN
    add x0, x0, x3, lsl #3
    add x1, x1, x3, lsl #3
    add x2, x2, x3, lsl #3
    cmn xzr, xzr                        // C = 0: no carry
    ADDSUB_LOOP adcs, x0, x1, x2, x3, x4, x5, x6, x7, x8, x9, x10, x11
    cset x0, cs                         // the carry: C
    ret
END Fullint64AddNWIN

ENTRY Fullint64SubNWIN
    add x0, x0, x3, lsl #3
    add x1, x1, x3, lsl #3
    add x2, x2, x3, lsl #3
    cmp xzr, xzr                        // C = 1: no borrow
    ADDSUB_LOOP sbcs, x0, x1, x2, x3, x4, x5, x6, x7, x8, x9, x10, x11
    cset x0, cc                         // the borrow: !C
    ret
END Fullint64SubNWIN

// =============================================================================
//  The engine's into-paths with the number's header written here too (x64 AddSubN.asm):
//    struct Header { uint32 off; uint32 size; uint8 neg; }   (FullInt's off_, size_, neg_)
//
//  void Fullint64AddNHdrWIN(const uint64* A, const uint64* B, uint64* R, Header* h)
//       in: h->size = n (>= 1). R[1 .. n] = A + B, R[0] = the carry; h->off = 1 - carry,
//       h->size = n + carry (h->neg untouched). R must not overlap A or B.
// =============================================================================
ENTRY Fullint64AddNHdrWIN
    ldr w4, [x3, #4]                    // n
    add x0, x0, x4, lsl #3
    add x1, x1, x4, lsl #3
    add x2, x2, #8                      // the sum from R[1]
    add x2, x2, x4, lsl #3
    mov x5, x4
    cmn xzr, xzr
    ADDSUB_LOOP adcs, x0, x1, x2, x5, x6, x7, x8, x9, x10, x11, x12, x13
    cset w6, cs
    str x6, [x2, #-8]                   // R[0] = the carry
    add w4, w4, w6                      // size = n + carry
    eor w6, w6, #1                      // off = 1 - carry
    stp w6, w4, [x3]
    ret
END Fullint64AddNHdrWIN

// =============================================================================
//  uint64 Fullint64SubCmpNWIN(const uint64* A, const uint64* B, uint64* R, int64 n)
//
//  |A - B| for equal lengths (n >= 1) in one call: the compare from the top, the order (the
//  larger minus the smaller), the subtraction and the length. The words from the first
//  differing one (index i) down are written to R at their own places, R[i .. n-1]; R[0 .. i-1]
//  is not touched. Returns
//    bits 0-31   off: the zero words above the difference (i + its leading zero words);
//                n when A == B (nothing written)
//    bit 63      1 when B > A (the operands swapped: R = B - A)
//  R may be A or B, but no other overlap.
//
//  void Fullint64SubCmpNHdrWIN(const uint64* A, const uint64* B, uint64* R, Header* h)
//       in: h->size = n (>= 1), h->neg = A's sign. |A - B| as above; h->off / h->size the
//       difference's place, h->neg flipped when B > A; A == B: R[0] = 0, off 0, size 1, neg false.
//
//  Registers: x0 / x1 A / B (then past the larger's / the smaller's low words), x2 R (then past
//  its low word), x3 n, x4 i (then off), x5 the swap, x6 the header, x7 the words left, x8-x15
//  the words.
// =============================================================================
.macro SUBCMPN hdr
    mov x4, xzr                         // i
1:  ldr x8, [x0, x4, lsl #3]
    ldr x9, [x1, x4, lsl #3]
    cmp x8, x9
    b.ne 3f
    add x4, x4, #1
    cmp x4, x3
    b.ne 1b
.if \hdr                                // A == B: zero
    str xzr, [x2]
    mov w8, #1
    stp wzr, w8, [x6]                   // off 0, size 1
    strb wzr, [x6, #8]                  // neg false
.else
    mov x0, x3                          // off = n
.endif
    ret
3:  cset x5, lo                         // B > A: swapped
    csel x8, x1, x0, lo                 // the larger
    csel x1, x0, x1, lo                 // the smaller
    add x0, x8, x3, lsl #3              // past their low words
    add x1, x1, x3, lsl #3
    add x2, x2, x3, lsl #3
    sub x7, x3, x4                      // the words from i down: n - i
    cmp xzr, xzr                        // C = 1: no borrow
    ADDSUB_LOOP sbcs, x0, x1, x2, x7, x8, x9, x10, x11, x12, x13, x14, x15
    ldr x8, [x2]                        // x2 = &R[i], the top word of the difference
    cbnz x8, 5f
4:  add x4, x4, #1                      // R[i] is zero only when A[i], B[i] differ by one:
    ldr x8, [x2, #8]!                   // the difference is nonzero, a nonzero word is below
    cbz x8, 4b
5:
.if \hdr
    sub w8, w3, w4
    stp w4, w8, [x6]                    // off, size = n - off
    ldrb w8, [x6, #8]
    eor w8, w8, w5
    strb w8, [x6, #8]                   // neg ^= swap
.else
    orr x0, x4, x5, lsl #63
.endif
    ret
.endm

ENTRY Fullint64SubCmpNWIN
    SUBCMPN 0
END Fullint64SubCmpNWIN

ENTRY Fullint64SubCmpNHdrWIN
    mov x6, x3                          // the header
    ldr w3, [x6, #4]                    // n
    SUBCMPN 1
END Fullint64SubCmpNHdrWIN
