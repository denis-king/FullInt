// =============================================================================
//  AArch64: the macros the kernels share (after abi.inc). Limbs big-endian (index 0 = the most
//  significant word): the loops run from the high address down, ldr / ldp pre-decrement.
//
//  Numeric local labels: the macros here use 70-99, the kernels' own code 1-69 - a macro's
//  labels then never catch a kernel's forward / backward reference (and the other way round).
// =============================================================================

// pa / pb / pr past the low words of A / B / R; n words of A op B with the carry in C (adcs /
// sbcs: C in and out), the pointers moved down n words, n consumed (0 allowed). One word if
// n & 1, two if n & 2, then groups of four (a group's words all read before any is written:
// R may be A or B). The loop control by sub / tbz / cbz / cbnz: the flags stay.
.macro ADDSUB_LOOP op, pa, pb, pr, n, w0, w1, w2, w3, v0, v1, v2, v3
    tbz \n, #0, 91f
    ldr \w0, [\pa, #-8]!
    ldr \v0, [\pb, #-8]!
    \op \w0, \w0, \v0
    str \w0, [\pr, #-8]!
91: tbz \n, #1, 92f
    ldp \w0, \w1, [\pa, #-16]!          // w0 the upper word, w1 the lower
    ldp \v0, \v1, [\pb, #-16]!
    \op \w1, \w1, \v1
    \op \w0, \w0, \v0
    stp \w0, \w1, [\pr, #-16]!
92: lsr \n, \n, #2
    cbz \n, 94f
93: ldp \w0, \w1, [\pa, #-16]
    ldp \v0, \v1, [\pb, #-16]
    ldp \w2, \w3, [\pa, #-32]!
    ldp \v2, \v3, [\pb, #-32]!
    \op \w1, \w1, \v1
    \op \w0, \w0, \v0
    \op \w3, \w3, \v3
    \op \w2, \w2, \v2
    stp \w0, \w1, [\pr, #-16]
    stp \w2, \w3, [\pr, #-32]!
    sub \n, \n, #1
    cbnz \n, 93b
94:
.endm

// the carry (C) through n words of A: pr[-1 ..] = pa[-1 ..] op 0 (adcs / sbcs), n consumed
.macro CARRY_RUN op, pa, pr, n, w
    cbz \n, 96f
95: ldr \w, [\pa, #-8]!
    \op \w, \w, xzr
    str \w, [\pr, #-8]!
    sub \n, \n, #1
    cbnz \n, 95b
96:
.endm

// dst = hi << sh | lo >> (64 - sh), any sh in 0..63: ns = -sh (the shift takes it mod 64),
// mask = 0 when sh = 0 (lo >> 64 would be lo, not 0), all ones otherwise. dst may be hi or lo.
.macro SHLD dst, hi, lo, sh, ns, mask, t
    lsr \t, \lo, \ns
    and \t, \t, \mask
    lsl \dst, \hi, \sh
    orr \dst, \dst, \t
.endm

// rp[-1 .. -n] = ap[-1 .. -n] * b; ap / rp move down n words (rp at the last word written),
// n (>= 1) -> 0, c the carry out (the row's next word up). A word product: mul (the low half)
// and umulh (the high half) - AArch64 has no 64 x 64 -> 128 in one instruction.
.macro MUL_1 ap, rp, n, b, c, t, lo, hi
    mov \c, xzr
81: ldr \t, [\ap, #-8]!
    mul \lo, \t, \b
    umulh \hi, \t, \b
    adds \lo, \lo, \c
    adc \c, \hi, xzr
    str \lo, [\rp, #-8]!
    subs \n, \n, #1
    b.ne 81b
.endm

// rp[-1 .. -n] += ap[-1 .. -n] * b; the same moves, c the carry out
.macro ADDMUL_1 ap, rp, n, b, c, t, lo, hi, w
    mov \c, xzr
82: ldr \t, [\ap, #-8]!
    ldr \w, [\rp, #-8]!
    mul \lo, \t, \b
    umulh \hi, \t, \b
    adds \lo, \lo, \c
    adc \hi, \hi, xzr
    adds \lo, \lo, \w
    adc \c, \hi, xzr
    str \lo, [\rp]
    subs \n, \n, #1
    b.ne 82b
.endm

// q = floor((u1 : u0) / d) for a normalized d (top bit set) and u1 < d, without a 128 / 64
// division instruction (AArch64 has udiv 64 / 64 only): long division in 32-bit digits (Knuth D
// with two digits of the quotient, each estimated by udiv from the divisor's top half and
// corrected by the next digit: at most two corrections; Hacker's Delight, divlu). For the
// reciprocals (once a division), not per word. u1 / u0 / d kept; the rest scratch.
.macro DIVLU q, u1, u0, d, dh, dl, un1, un0, rh, t, t2
    lsr \dh, \d, #32
    and \dl, \d, #0xffffffff
    lsr \un1, \u0, #32
    and \un0, \u0, #0xffffffff
    udiv \q, \u1, \dh                   // q1
    msub \rh, \q, \dh, \u1              // its remainder
71: lsr \t, \q, #32                     // q1 >= 2^32 or q1 * dl > (rh : un1): one too many
    cbnz \t, 72f
    mul \t, \q, \dl
    orr \t2, \un1, \rh, lsl #32
    cmp \t, \t2
    b.ls 73f
72: sub \q, \q, #1
    add \rh, \rh, \dh
    lsr \t, \rh, #32
    cbz \t, 71b                         // rh >= 2^32: the test holds from here
73: lsl \t, \u1, #32
    orr \t, \t, \un1
    msub \t, \q, \d, \t                 // (u1 : un1) - q1 * d: the next partial remainder
    lsl \un1, \q, #32                   // q1 in the upper half
    udiv \q, \t, \dh                    // q0
    msub \rh, \q, \dh, \t
74: lsr \t, \q, #32
    cbnz \t, 75f
    mul \t, \q, \dl
    orr \t2, \un0, \rh, lsl #32
    cmp \t, \t2
    b.ls 76f
75: sub \q, \q, #1
    add \rh, \rh, \dh
    lsr \t, \rh, #32
    cbz \t, 74b
76: orr \q, \q, \un1
.endm

// v = floor((b^2 - 1) / d) - b for a normalized d (b = 2^64): the 2-by-1 reciprocal (Moller,
// Granlund: "Improved division by invariant integers", 2011) = (b - 1 - d : b - 1) / d
.macro RECIP v, d, u1, u0, dh, dl, un1, un0, rh, t, t2
    mvn \u1, \d
    mov \u0, #-1
    DIVLU \v, \u1, \u0, \d, \dh, \dl, \un1, \un0, \rh, \t, \t2
.endm

// the 3-by-2 reciprocal from the 2-by-1 one of d1: v for (d1 : d0), both normalized (the same
// paper, the adjustment for the second word) - v in, v out
.macro RECIP_3BY2 v, d1, d0, p, t0, t1
    mul \p, \d1, \v
    adds \p, \p, \d0                    // p = d1 * v + d0 (mod b)
    b.cc 79f
    sub \v, \v, #1
    cmp \p, \d1
    b.lo 78f
    sub \v, \v, #1
    sub \p, \p, \d1
78: sub \p, \p, \d1
79: mul \t0, \d0, \v
    umulh \t1, \d0, \v                  // t1 : t0 = d0 * v
    adds \p, \p, \t1
    b.cc 70f
    sub \v, \v, #1
    cmp \p, \d1
    b.lo 70f
    b.hi 80f
    cmp \t0, \d0
    b.lo 70f
80: sub \v, \v, #1
70:
.endm

// one quotient word of the 2-by-1 division with the reciprocal v (the same paper, Algorithm 4):
// (u1 : u0) / d, u1 < d, d normalized -> q; the remainder in u1.
//    (q1 : q0) = v * u1 + (u1 + 1 : u0);  r = u0 - q1 * d
//    r > q0: q1 - 1, r + d;  r >= d (rare): q1 + 1, r - d
.macro STEP_2BY1 q, u1, u0, d, v, q0, t
    mul \q0, \v, \u1
    umulh \q, \v, \u1
    adds \q0, \q0, \u0
    adc \q, \q, \u1
    add \q, \q, #1
    msub \u1, \q, \d, \u0               // r (mod b)
    cmp \u1, \q0
    csetm \t, hi                        // r > q0: one too many
    add \q, \q, \t
    and \t, \t, \d
    add \u1, \u1, \t
    cmp \u1, \d
    b.lo 77f
    sub \u1, \u1, \d                    // rare: one too few
    add \q, \q, #1
77:
.endm
