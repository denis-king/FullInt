    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Exact division by a small constant for the Toom-Cook interpolation (AArch64), the contract
//  of x64 ToomOps.asm:
//    uint64 Fullint64DivExactWIN(a, q, int len, uint64 d)
//      q = a / d for an a known to be a multiple of d; d = d'·2^t with d' odd, below 2^32;
//      q may be a. Hensel division from the low end on x = a >> t (each word with the low
//      bits of the next one up): inv = d'^-1 mod 2^64, per word
//        q_i = (x_i - c)·inv,  c' = hi(q_i·d') + (x_i < c)
//      Returns the last c: 0 when a was a multiple.
//  inv: (3d') xor 2 is right in 5 bits, each x·(2 - d'·x) doubles them (four steps: 80 bits).
//
//  Registers: x0 a, x1 q, x2 the word index (from the low end), x3 d', x4 t, x5 = -t,
//  x6 the mask (0 when t = 0), x7 inv, x8 c, x9-x11 words.
// =============================================================================

ENTRY Fullint64DivExactWIN
    sxtw x2, w2
    rbit x4, x3
    clz x4, x4                          // t (d != 0)
    lsr x3, x3, x4                      // d'
    neg x5, x4
    cmp x4, #0
    csetm x6, ne
    add x7, x3, x3, lsl #1
    eor x7, x7, #2                      // 5 bits of the inverse
    .rept 4
    mul x9, x3, x7
    neg x9, x9
    add x9, x9, #2                      // 2 - d'·x
    mul x7, x7, x9
    .endr
    mov x8, xzr                         // c
    subs x2, x2, #1                     // the low word
    b.lt 3f
    b.eq 2f
1:  add x10, x0, x2, lsl #3
    ldp x11, x9, [x10, #-8]             // the next word up, the word
    lsr x9, x9, x4
    lsl x11, x11, x5
    and x11, x11, x6
    orr x9, x9, x11                     // x_i
    subs x9, x9, x8                     // x_i - c
    cset x10, lo                        // the borrow
    mul x9, x9, x7                      // q_i
    str x9, [x1, x2, lsl #3]
    umulh x8, x9, x3
    add x8, x8, x10
    subs x2, x2, #1
    b.ne 1b
2:  ldr x9, [x0]                        // the top word: nothing above it
    lsr x9, x9, x4
    subs x9, x9, x8
    cset x10, lo
    mul x9, x9, x7
    str x9, [x1]
    umulh x8, x9, x3
    add x8, x8, x10
3:  mov x0, x8
    ret
END Fullint64DivExactWIN
