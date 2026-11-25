    .include "abi.inc"

// =============================================================================
//  Number-theoretic transform kernels (AArch64): the contracts of x64 NttAvx2.asm. On NEON,
//  4 lanes of 32 bits: the radix-2 and radix-3 levels, the pointwise product, the scaling;
//  scalar: the load and the CRT (the reference: FullInt.Cpp/src/ntt_portable.cpp).
//
//  Three primes p < 2^30 (4p < 2^32); the values lazy in [0, 2p). The radix-2 levels' twiddles
//  in Shoup's form (Ntt.cpp's AArch64 tables: w itself, wq = floor(w·2^32 / p); MULS, five
//  instructions): q = hi(a·wq), r = a·w - q·p (mod 2^32) in [0, 2p) for any a < 2^32. The
//  pointwise product (two transforms, no precomputed factor) and the scaling in Montgomery's
//  form, wq = w·p^-1 mod 2^32 (MULW, nine):
//    m = a·wq mod 2^32;  r = hi(a·w) - hi(m·p) in (-p, p);  r + p in [0, 2p)
//  hi(a·w) of four lanes: umull / umull2 (two 64-bit products each), uzp2 takes the high halves.
//  [0, 4p) -> [0, 2p): min(v, v - 2p) (unsigned: v - 2p wraps when v < 2p).
//
//  The levels h >= 8 one at a time over the whole array (L a multiple of 16), one vector of j a
//  step. Dif3 / Dit3: the levels 4, 2, 1 (1, 2, 4) inside the registers on 16 values a step
//  (LAST3), the values shuffled between the levels by zip / uzp; the order the forward
//  transform leaves is undone by the inverse (the pointwise product does not care).
//
//    void Fullint64NttDif1WIN_neon(uint32* x, int64 L, int64 h, const uint32* W, const uint32* Wq, uint32 p)
//    void Fullint64NttDif2WIN_neon(... same ...)       levels h, h/2
//    void Fullint64NttDif3WIN_neon(uint32* x, int64 L, const uint32* W, const uint32* Wq, uint32 p)
//    void Fullint64NttDit1WIN_neon / Dit2 (levels h/2, h) / Dit3: the same, the inverse tables
//    void Fullint64NttPointWIN_neon(uint32* x, const uint32* y, int64 n, uint32 p, uint32 pinv)
//    void Fullint64NttScaleWIN_neon(uint32* x, int64 n, uint32 p, uint32 c, uint32 cq)
//
//  v31 = p, v30 = 2p; v0-v7, v16-v26 scratch (v8-v15 untouched: callee-saved).
// =============================================================================

// r = a·w·2^-32 mod p in [0, 2p); a < 4p. r distinct from t0-t2; a / w / wq may be reused after.
.macro MULW r, a, w, wq, t0, t1, t2
    umull \t0\().2d, \a\().2s, \w\().2s
    umull2 \t1\().2d, \a\().4s, \w\().4s
    uzp2 \t2\().4s, \t0\().4s, \t1\().4s        // hi(a·w)
    mul \t0\().4s, \a\().4s, \wq\().4s          // m
    umull \t1\().2d, \t0\().2s, v31.2s
    umull2 \t0\().2d, \t0\().4s, v31.4s
    uzp2 \t1\().4s, \t1\().4s, \t0\().4s        // hi(m·p)
    sub \r\().4s, \t2\().4s, \t1\().4s
    add \r\().4s, \r\().4s, v31.4s
.endm

// r = a·w mod p in [0, 2p), the twiddle in Shoup's form; r distinct from a, t0, t1
.macro MULS r, a, w, wq, t0, t1
    umull \t0\().2d, \a\().2s, \wq\().2s
    umull2 \t1\().2d, \a\().4s, \wq\().4s
    uzp2 \t0\().4s, \t0\().4s, \t1\().4s        // q = hi(a·wq)
    mul \r\().4s, \a\().4s, \w\().4s
    mls \r\().4s, \t0\().4s, v31.4s             // a·w - q·p
.endm

// v = min(v, v - 2p)
.macro RED v, t
    sub \t\().4s, \v\().4s, v30.4s
    umin \v\().4s, \v\().4s, \t\().4s
.endm

// DIF: (u, v) -> (u + v, (u - v)·w); results in u (sum) and v (product); s, d, t, m0, m1 scratch
.macro DIF_BFLY u, v, w, wq, s=v4, d=v5, t=v6, m0=v16, m1=v17
    add \s\().4s, \u\().4s, \v\().4s
    sub \d\().4s, \u\().4s, \v\().4s
    add \d\().4s, \d\().4s, v30.4s              // u - v + 2p in (0, 4p)
    RED \s, \t
    MULS \v, \d, \w, \wq, \m0, \m1
    mov \u\().16b, \s\().16b
.endm

// DIT: (u, v) -> (u + v·w, u - v·w)
.macro DIT_BFLY u, v, w, wq, s=v4, d=v5, t=v6, m0=v16, m1=v17
    MULS \s, \v, \w, \wq, \m0, \m1              // v·w in [0, 2p)
    add \d\().4s, \u\().4s, \s\().4s
    sub \v\().4s, \u\().4s, \s\().4s
    add \v\().4s, \v\().4s, v30.4s
    RED \d, \t
    RED \v, \t
    mov \u\().16b, \d\().16b
.endm

// the twiddle 1 (the level h = 1: W[1] = w_2^0, Shoup's form keeps it 1): (u, v) -> (u + v,
// u - v), no product - DIF and DIT alike
.macro BFLY1 u, v, s=v4, t=v6
    add \s\().4s, \u\().4s, \v\().4s
    sub \v\().4s, \u\().4s, \v\().4s
    add \v\().4s, \v\().4s, v30.4s
    RED \s, \t
    RED \v, \t
    mov \u\().16b, \s\().16b
.endm

.macro SETUP_P p
    dup v31.4s, \p
    add v30.4s, v31.4s, v31.4s
.endm

// one level h >= 8 over x[0 .. L): x0 x, x1 L, x2 h, x3 W, x4 Wq; dif = 1 / 0. Two vectors of j
// a step: two butterflies on their own registers, side by side.
.macro LEVEL dif
    add x9, x3, x2, lsl #2              // &W[h]
    add x10, x4, x2, lsl #2             // &Wq[h]
    mov x5, x0                          // the block
    add x6, x0, x1, lsl #2              // the end
1:  mov x7, x5                          // x[b + j]
    add x8, x5, x2, lsl #2              // x[b + j + h]
    mov x11, x9
    mov x12, x10
    mov x13, x2
2:  ld1 {v0.4s, v1.4s}, [x7]           // u: j .. j + 7
    ld1 {v2.4s, v3.4s}, [x8]            // v
    ld1 {v18.4s, v19.4s}, [x11], #32    // W
    ld1 {v20.4s, v21.4s}, [x12], #32    // Wq
.if \dif
    DIF_BFLY v0, v2, v18, v20
    DIF_BFLY v1, v3, v19, v21, v22, v23, v24, v25, v26
.else
    DIT_BFLY v0, v2, v18, v20
    DIT_BFLY v1, v3, v19, v21, v22, v23, v24, v25, v26
.endif
    st1 {v0.4s, v1.4s}, [x7], #32
    st1 {v2.4s, v3.4s}, [x8], #32
    subs x13, x13, #8
    b.ne 2b
    add x5, x5, x2, lsl #3              // the next block: 2h values on
    cmp x5, x6
    b.lo 1b
.endm

// the levels 4, 2, 1 inside the registers, two blocks of 8 a step (x0 x, x1 L, x2 W, x3 Wq):
//   A, B = a0..a3, a4..a7       level 4: (A, B) with W[4..7]
//   U = a0 a1 a4 a5, V = a2 a3 a6 a7 (zip of 64-bit halves)    level 2: W[2], W[3] twice
//   P = a0 a4 a2 a6, Q = a1 a5 a3 a7 (uzp of 32-bit lanes)     level 1: W[1] = 1, no product
// stored as P, Q: the inverse (dif = 0) loads them so, runs the levels 1, 2, 4 and undoes the
// shuffles on the way (zip of lanes, zip of halves), storing in natural order.
.macro TWIDDLES_LAST3
    ldr q20, [x2, #16]                  // W[4..7]
    ldr q21, [x3, #16]
    ldr d22, [x2, #8]                   // W[2], W[3], twice
    mov v22.d[1], v22.d[0]
    ldr d23, [x3, #8]
    mov v23.d[1], v23.d[0]
.endm

.macro LAST3 dif
    TWIDDLES_LAST3
3:  ld1 {v0.4s, v1.4s, v2.4s, v3.4s}, [x0]
.if \dif
    DIF_BFLY v0, v1, v20, v21           // level 4
    DIF_BFLY v2, v3, v20, v21
    zip1 v19.2d, v0.2d, v1.2d           // U, V
    zip2 v1.2d, v0.2d, v1.2d
    mov v0.16b, v19.16b
    zip1 v19.2d, v2.2d, v3.2d
    zip2 v3.2d, v2.2d, v3.2d
    mov v2.16b, v19.16b
    DIF_BFLY v0, v1, v22, v23           // level 2
    DIF_BFLY v2, v3, v22, v23
    uzp1 v19.4s, v0.4s, v1.4s           // P, Q
    uzp2 v1.4s, v0.4s, v1.4s
    mov v0.16b, v19.16b
    uzp1 v19.4s, v2.4s, v3.4s
    uzp2 v3.4s, v2.4s, v3.4s
    mov v2.16b, v19.16b
    BFLY1 v0, v1                        // level 1 (w = 1)
    BFLY1 v2, v3
.else
    BFLY1 v0, v1                        // level 1 on P, Q (w = 1)
    BFLY1 v2, v3
    zip1 v19.4s, v0.4s, v1.4s           // U, V
    zip2 v1.4s, v0.4s, v1.4s
    mov v0.16b, v19.16b
    zip1 v19.4s, v2.4s, v3.4s
    zip2 v3.4s, v2.4s, v3.4s
    mov v2.16b, v19.16b
    DIT_BFLY v0, v1, v22, v23           // level 2
    DIT_BFLY v2, v3, v22, v23
    zip1 v19.2d, v0.2d, v1.2d           // A, B
    zip2 v1.2d, v0.2d, v1.2d
    mov v0.16b, v19.16b
    zip1 v19.2d, v2.2d, v3.2d
    zip2 v3.2d, v2.2d, v3.2d
    mov v2.16b, v19.16b
    DIT_BFLY v0, v1, v20, v21           // level 4
    DIT_BFLY v2, v3, v20, v21
.endif
    st1 {v0.4s, v1.4s, v2.4s, v3.4s}, [x0], #64
    subs x1, x1, #16
    b.ne 3b
.endm

ENTRY Fullint64NttDif1WIN_neon
    SETUP_P w5
    LEVEL 1
    ret
END Fullint64NttDif1WIN_neon

ENTRY Fullint64NttDif2WIN_neon
    SETUP_P w5
    LEVEL 1
    lsr x2, x2, #1
    LEVEL 1
    ret
END Fullint64NttDif2WIN_neon

ENTRY Fullint64NttDif3WIN_neon
    SETUP_P w4
    LAST3 1
    ret
END Fullint64NttDif3WIN_neon

ENTRY Fullint64NttDit1WIN_neon
    SETUP_P w5
    LEVEL 0
    ret
END Fullint64NttDit1WIN_neon

ENTRY Fullint64NttDit2WIN_neon
    SETUP_P w5
    lsr x2, x2, #1
    LEVEL 0
    lsl x2, x2, #1
    LEVEL 0
    ret
END Fullint64NttDit2WIN_neon

ENTRY Fullint64NttDit3WIN_neon
    SETUP_P w4
    LAST3 0
    ret
END Fullint64NttDit3WIN_neon

// x = x·y·2^-32 in [0, 2p): x0 x, x1 y, x2 n, w3 p, w4 pinv
ENTRY Fullint64NttPointWIN_neon
    SETUP_P w3
    dup v7.4s, w4
5:  ld1 {v0.4s}, [x0]
    ld1 {v1.4s}, [x1], #16
    mul v3.4s, v1.4s, v7.4s             // y·p^-1
    MULW v2, v0, v1, v3, v16, v17, v18
    st1 {v2.4s}, [x0], #16
    subs x2, x2, #4
    b.ne 5b
    ret
END Fullint64NttPointWIN_neon

// x = x·c·2^-32 in [0, p): x0 x, x1 n, w2 p, w3 c, w4 cq
ENTRY Fullint64NttScaleWIN_neon
    SETUP_P w2
    dup v1.4s, w3
    dup v3.4s, w4
6:  ld1 {v0.4s}, [x0]
    MULW v2, v0, v1, v3, v16, v17, v18
    sub v4.4s, v2.4s, v31.4s
    umin v2.4s, v2.4s, v4.4s            // [0, 2p) -> [0, p)
    st1 {v2.4s}, [x0], #16
    subs x1, x1, #4
    b.ne 6b
    ret
END Fullint64NttScaleWIN_neon

// -----------------------------------------------------------------------------
// The radix-3 level of a transform of N = 3M values (x64 NttAvx2.asm has the formulas), four j
// a step. k (Montgomery form): [0..7] w^0..7, [8..15] w^0,2,..,14, [16] w^8, [24] its q,
// [48] ω, [56] its q, [64] p^-1. The twiddles w^j, w^2j made as the loop goes: times w^4 and
// w^8 a step, kept below p; their q (·p^-1) computed a step. x0 x, x1 M, x2 k, w3 p.
// Registers: v29 p^-1, v28 / v27 ω and its q, v26 / v25 w^4 and its q, v24 / v23 w^8 and its
// q, v22 w^j, v21 w^2j; v0-v7, v16-v20 scratch.
.macro R3_SETUP
    SETUP_P w3
    ldr q29, [x2, #256]                 // p^-1
    ldr q28, [x2, #192]                 // ω
    ldr q27, [x2, #224]
    ldr q22, [x2]                       // w^0..3
    ldr q21, [x2, #32]                  // w^0,2,4,6
    add x9, x2, #16
    ld1r {v26.4s}, [x9]                 // w^4
    mul v25.4s, v26.4s, v29.4s
    ldr q24, [x2, #64]                  // w^8
    ldr q23, [x2, #96]
    add x4, x0, x1, lsl #2              // x + M
    add x5, x4, x1, lsl #2              // x + 2M
.endm

// the twiddles four j on: w^j·w^4, w^2j·w^8, below p
.macro R3_STEP
    MULW v3, v22, v26, v25, v16, v17, v18
    sub v6.4s, v3.4s, v31.4s
    umin v22.4s, v3.4s, v6.4s
    MULW v3, v21, v24, v23, v16, v17, v18
    sub v6.4s, v3.4s, v31.4s
    umin v21.4s, v3.4s, v6.4s
.endm

// y0 = x0 + x1 + x2, y1 = (x0 - x2 + ω·(x1 - x2))·w^j, y2 = (x0 - x1 - ω·(x1 - x2))·w^2j
ENTRY Fullint64NttR3DifWIN_neon
    R3_SETUP
7:  ldr q0, [x0]
    ldr q1, [x4]
    ldr q2, [x5]
    sub v3.4s, v1.4s, v2.4s
    add v3.4s, v3.4s, v30.4s
    MULW v4, v3, v28, v27, v16, v17, v18    // u = ω·(x1 - x2)
    add v5.4s, v0.4s, v1.4s                 // y0
    RED v5, v6
    add v5.4s, v5.4s, v2.4s
    RED v5, v6
    sub v3.4s, v0.4s, v2.4s                 // y1
    add v3.4s, v3.4s, v30.4s
    RED v3, v6
    add v3.4s, v3.4s, v4.4s
    mul v19.4s, v22.4s, v29.4s
    MULW v7, v3, v22, v19, v16, v17, v18
    sub v3.4s, v0.4s, v1.4s                 // y2
    add v3.4s, v3.4s, v30.4s
    RED v3, v6
    sub v3.4s, v3.4s, v4.4s
    add v3.4s, v3.4s, v30.4s
    mul v19.4s, v21.4s, v29.4s
    MULW v20, v3, v21, v19, v16, v17, v18
    str q5, [x0], #16
    str q7, [x4], #16
    str q20, [x5], #16
    R3_STEP
    subs x1, x1, #4
    b.ne 7b
    ret
END Fullint64NttR3DifWIN_neon

// y1' = y1·w^j, y2' = y2·w^2j (w and ω inverted); x0 = y0 + y1' + y2',
// x1 = y0 - y2' + ω·(y1' - y2'), x2 = y0 - y1' - ω·(y1' - y2')
ENTRY Fullint64NttR3DitWIN_neon
    R3_SETUP
8:  ldr q0, [x0]
    ldr q1, [x4]
    ldr q2, [x5]
    mul v19.4s, v22.4s, v29.4s
    MULW v3, v1, v22, v19, v16, v17, v18    // y1'
    mul v19.4s, v21.4s, v29.4s
    MULW v7, v2, v21, v19, v16, v17, v18    // y2'
    sub v4.4s, v3.4s, v7.4s
    add v4.4s, v4.4s, v30.4s
    MULW v20, v4, v28, v27, v16, v17, v18   // u = ω·(y1' - y2')
    add v5.4s, v0.4s, v3.4s                 // x0
    RED v5, v16
    add v5.4s, v5.4s, v7.4s
    RED v5, v16
    sub v4.4s, v0.4s, v7.4s                 // x1
    add v4.4s, v4.4s, v30.4s
    RED v4, v16
    add v4.4s, v4.4s, v20.4s
    RED v4, v16
    sub v6.4s, v0.4s, v3.4s                 // x2
    add v6.4s, v6.4s, v30.4s
    RED v6, v16
    sub v6.4s, v6.4s, v20.4s
    add v6.4s, v6.4s, v30.4s
    RED v6, v16
    str q5, [x0], #16
    str q4, [x4], #16
    str q6, [x5], #16
    R3_STEP
    subs x1, x1, #4
    b.ne 8b
    ret
END Fullint64NttR3DitWIN_neon

// -----------------------------------------------------------------------------
// void Fullint64NttLoadWIN_neon(const uint64* a, int64 la, uint32* x, int64 L, uint32 p)
// big-endian words -> little-endian 32-bit digits in [0, 2p) (a digit < 2^32 < 5p: minus 2p
// twice at most), then zeros up to L. x0 a, x1 la, x2 x, x3 L, w4 p.
ENTRY Fullint64NttLoadWIN_neon
    lsl w5, w4, #1                      // 2p
    add x6, x0, x1, lsl #3              // past a's lowest word
    mov x7, x1
    cbz x7, 2f
1:  ldr x8, [x6, #-8]!
    lsr x9, x8, #32
    mov w8, w8
    subs w10, w8, w5
    csel w8, w10, w8, hs
    subs w10, w8, w5
    csel w8, w10, w8, hs
    subs w10, w9, w5
    csel w9, w10, w9, hs
    subs w10, w9, w5
    csel w9, w10, w9, hs
    stp w8, w9, [x2], #8
    subs x7, x7, #1
    b.ne 1b
2:  sub x3, x3, x1, lsl #1              // the zeros
    cbz x3, 4f
3:  str wzr, [x2], #4
    subs x3, x3, #1
    b.ne 3b
4:  ret
END Fullint64NttLoadWIN_neon

// -----------------------------------------------------------------------------
// uint64 Fullint64NttCrtWIN_neon(const uint32* x0, const uint32* x1, const uint32* x2, int64 nd,
//                                uint64* r, int64 len, const uint64* k)
// Garner over the three residues (each in [0, p)) of nd = 2·len digits: v1 = x0,
// v2 = (x1 - v1)·p1^-1 mod p2, v3 = (x2 - v1 - v2·p1)·(p1p2)^-1 mod p3; the products by the
// constants by Shoup's method (k: p1, p2, p3, inv12, inv12s, p1m3, p1m3s, inv123, inv123s, p1p2;
// s = floor(c·2^32 / p): q = hi32(a·s), a·c - q·p in [0, 2p)), the reductions by subtraction
// (p1 < 2·p2, p1 < 2·p3). Two digits a step: acc += value(2w) + value(2w + 1)·2^32 (each value
// v1 + p1·v2 + p1p2·v3 < 2^90; acc < 2^123), the low word out to r from its low end up, acc >>= 64.
// Returns the carry out of the top word.
// Registers: x9-x17, x19 the constants (p1 p2 p3 inv12 inv12s p1m3 p1m3s inv123 inv123s, p1p2),
// x20 / x21 acc, x22-x26 scratch, x27 / x28 a value; x19-x28 saved.
.macro CRT_VALUE lo, hi
    ldr w22, [x0], #4                   // v1
    ldr w23, [x1], #4
    ldr w24, [x2], #4
    subs x25, x22, x10
    csel x25, x25, x22, hs              // v1 mod p2
    add x23, x23, x10
    sub x23, x23, x25                   // d = x1 - v1 + p2 in (0, 2·p2)
    subs x25, x23, x10
    csel x23, x25, x23, hs
    mul x25, x23, x13                   // v2 = d·inv12 mod p2
    lsr x25, x25, #32
    mul x23, x23, x12
    msub x23, x25, x10, x23
    subs x25, x23, x10
    csel x23, x25, x23, hs
    mul x25, x23, x15                   // v2·p1 mod p3
    lsr x25, x25, #32
    mul x26, x23, x14
    msub x26, x25, x11, x26
    subs x25, x26, x11
    csel x26, x25, x26, hs
    subs x25, x22, x11
    csel x25, x25, x22, hs              // v1 mod p3
    add x24, x24, x11, lsl #1
    sub x24, x24, x25
    sub x24, x24, x26                   // t = x2 - v1 - v2·p1 + 2·p3 in (0, 3·p3)
    subs x25, x24, x11
    csel x24, x25, x24, hs
    subs x25, x24, x11
    csel x24, x25, x24, hs
    mul x25, x24, x17                   // v3 = t·inv123 mod p3
    lsr x25, x25, #32
    mul x24, x24, x16
    msub x24, x25, x11, x24
    subs x25, x24, x11
    csel x24, x25, x24, hs
    mul \lo, x9, x23                    // p1·v2 (< 2^60) + v1
    add \lo, \lo, x22
    mul x25, x19, x24                   // + p1p2·v3 (< 2^90)
    umulh \hi, x19, x24
    adds \lo, \lo, x25
    adc \hi, \hi, xzr
.endm

ENTRY Fullint64NttCrtWIN_neon
    stp x19, x20, [sp, #-80]!
    stp x21, x22, [sp, #16]
    stp x23, x24, [sp, #32]
    stp x25, x26, [sp, #48]
    stp x27, x28, [sp, #64]
    ldp x9, x10, [x6]
    ldp x11, x12, [x6, #16]
    ldp x13, x14, [x6, #32]
    ldp x15, x16, [x6, #48]
    ldp x17, x19, [x6, #64]
    mov x20, xzr
    mov x21, xzr
    add x4, x4, x5, lsl #3              // past r's end (its low word)
    cbz x5, 2f
1:  CRT_VALUE x27, x28                  // digit 2w
    adds x20, x20, x27
    adc x21, x21, x28
    CRT_VALUE x27, x28                  // digit 2w + 1, times 2^32
    lsl x26, x27, #32
    extr x28, x28, x27, #32
    adds x20, x20, x26
    adc x21, x21, x28
    str x20, [x4, #-8]!
    mov x20, x21
    mov x21, xzr
    subs x5, x5, #1
    b.ne 1b
2:  mov x0, x20
    ldp x21, x22, [sp, #16]
    ldp x23, x24, [sp, #32]
    ldp x25, x26, [sp, #48]
    ldp x27, x28, [sp, #64]
    ldp x19, x20, [sp], #80
    ret
END Fullint64NttCrtWIN_neon
