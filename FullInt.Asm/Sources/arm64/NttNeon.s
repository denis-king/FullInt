    .include "abi.inc"

// =============================================================================
//  Number-theoretic transform kernels on NEON (AArch64), 4 lanes of 32 bits: the contracts of
//  x64 NttAvx2.asm (the radix-2 levels, the pointwise product, the scaling); the radix-3
//  level, the load and the CRT stay in C++ (FullInt.Cpp/src/ntt_portable.cpp).
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
//   P = a0 a4 a2 a6, Q = a1 a5 a3 a7 (uzp of 32-bit lanes)     level 1: W[1]
// stored as P, Q: the inverse (dif = 0) loads them so, runs the levels 1, 2, 4 and undoes the
// shuffles on the way (zip of lanes, zip of halves), storing in natural order.
.macro TWIDDLES_LAST3
    ldr q20, [x2, #16]                  // W[4..7]
    ldr q21, [x3, #16]
    ldr d22, [x2, #8]                   // W[2], W[3], twice
    mov v22.d[1], v22.d[0]
    ldr d23, [x3, #8]
    mov v23.d[1], v23.d[0]
    add x9, x2, #4                      // W[1]
    ld1r {v24.4s}, [x9]
    add x9, x3, #4
    ld1r {v25.4s}, [x9]
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
    DIF_BFLY v0, v1, v24, v25           // level 1
    DIF_BFLY v2, v3, v24, v25
.else
    DIT_BFLY v0, v1, v24, v25           // level 1 on P, Q
    DIT_BFLY v2, v3, v24, v25
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
