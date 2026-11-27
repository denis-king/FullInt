// =============================================================================
//  ARMv7: division by one word and the decimal conversions (the contracts of kernels.h):
//    void Fullint64Div2longWin(const uint64_t* a, uint64_t d, uint64_t* q, uint64_t* r, int lenA)
//    void Fullint64ToDecStringWin(const uint64_t* value, uint64_t* valueBuf, uint8_t* resultBuf,
//                                 int lenValue, int* lenResult)
//    void Fullint64FromDecStringWin(const uint8_t* value, uint64_t* valueBuf, uint64_t* resultBuf,
//                                   int lenValue, int* lenResult)
//  (the ..._recip names are the same code: these already divide by a reciprocal).
//
//  Division in 32-bit digits by a reciprocal (Moller, Granlund 2011): the divisor normalized to
//  two digits (d1 d0), v = floor((b^3 - 1) / (d1 b + d0)) - b with b = 2^32, and one 3-by-2
//  step per dividend digit - two umull, the corrections branch-free but for a rare one. ARMv7
//  need not have udiv: v comes from a 32-step restoring division (once a call) and invert_pi1's
//  adjustment for d0. 10^19 is normalized already: its v is a constant.
// =============================================================================

    .include "abi.inc"
    .fpu vfpv3-d16              // the reciprocal's first guess (armhf has VFPv3)

.macro ALIAS name
    .globl \name
    .type \name, %function
\name:
.endm

// v of (d1, d0), d1 normalized: r0 = d1, r1 = d0 -> r0 = v; r1 kept, r2, r3, r12 used
    .p2align 4
.Linvert:
    push {r4, lr}
    // the 2-by-1 reciprocal of d1, floor((b^2 - 1) / d1) - b: (double) 2^64 / d1 - 2^32 (VFP,
    // armhf's baseline; within 1 of it), then made exact by its product
    vmov s0, r0
    vcvt.f64.u32 d0, s0
    mov r2, #0
    movw r3, #0x0000
    movt r3, #0x43F0            // 2^64
    vmov d1, r2, r3
    vdiv.f64 d1, d1, d0
    movt r3, #0x41F0            // 2^32
    vmov d2, r2, r3
    vsub.f64 d1, d1, d2
    vcvt.u32.f64 s0, d1         // toward zero
    vmov r3, s0
6:  umull r2, r12, r3, r0       // (b + v)·d1 = v·d1 + d1·b: over b^2 - 1 -> v - 1
    adds r12, r12, r0
    subcs r3, r3, #1
    bcs 6b
    mvn r2, r2                  // b^2 - 1 - (b + v)·d1 >= d1 -> v + 1
    mvn r12, r12
    cmp r12, #0                 // (a nonzero high word: HS)
    cmpeq r2, r0
    addhs r3, r3, #1
    bhs 6b
    mul r2, r0, r3              // invert_pi1: p = d1 v + d0 (mod b)
    adds r2, r2, r1
    bcc 3f
    sub r3, r3, #1
    cmp r2, r0
    blo 2f
    sub r3, r3, #1
    sub r2, r2, r0
2:  sub r2, r2, r0
3:  umull r12, r4, r1, r3       // t1:t0 = d0 v
    adds r2, r2, r4
    bcc 5f
    sub r3, r3, #1
    cmp r2, r0
    blo 5f
    bhi 4f
    cmp r12, r1
    blo 5f
4:  sub r3, r3, #1
5:  mov r0, r3
    pop {r4, pc}

// One 3-by-2 step: (u2 u1 u0) / (d1 d0) with (u2 u1) < (d1 d0); u2 = r8, u1 = r7, d0 = r4,
// d1 = r5, v = r6. Out: the quotient digit in \qout, the remainder in (r8 r7); r0-r3 used.
.macro DIV3BY2 u0, qout
    umull r0, r1, r6, r8        // (q1 q0) = v u2 + (u2 u1)
    adds r0, r0, r7
    adc r1, r1, r8
    mls r8, r1, r5, r7          // r1 = u1 - q1 d1
    umull r2, r3, r4, r1        // t = d0 q1
    subs r7, \u0, r2            // (r1 r0) = (r1 u0) - t - (d1 d0)
    sbc r8, r8, r3
    subs r7, r7, r4
    sbc r8, r8, r5
    add r1, r1, #1
    cmp r8, r0                  // r1 >= q0: q1 - 1, the remainder + d (a mask, no branch)
    mvnhs r2, #0
    movlo r2, #0
    and r3, r2, r4
    adds r7, r7, r3
    and r3, r2, r5
    adc r8, r8, r3
    add \qout, r1, r2
    cmp r8, r5                  // the remainder >= d (rare): q1 + 1, the remainder - d
    cmpeq r7, r4
    blo .Lok\@
    add \qout, \qout, #1
    subs r7, r7, r4
    sbc r8, r8, r5
.Lok\@:
.endm

// In place over r10 >= 1 words from r9 (the top word first): each word / (d1 d0) by two steps,
// the quotient word over it; the remainder (r8 r7) in (below d) and out. r9 moved past the
// words; r0-r3, r10-r12 used.
    .p2align 4
.Ldivpass:
    push {lr}
1:  ldr r11, [r9]               // the word's low digit
    ldr lr, [r9, #4]            // its high digit
    DIV3BY2 lr, r12
    DIV3BY2 r11, lr
    str lr, [r9]
    str r12, [r9, #4]
    add r9, r9, #8
    subs r10, r10, #1
    bne 1b
    pop {pc}

// -----------------------------------------------------------------------------
// Div2long: a / d (q may be a). q = A << s word by word (the overflow word kept), then the
// division pass over q in place, the remainder >> s.
.equ D_S, 0
.equ D_A, 4
.equ D_D0, 8
.equ D_D1, 12
.equ D_V, 16
.equ D_OLO, 20
.equ D_OHI, 24
.equ D_SIZE, 32
.equ D_Q, D_SIZE + 36
.equ D_R, D_SIZE + 40
.equ D_LEN, D_SIZE + 44

ENTRY Fullint64Div2longWin
ALIAS Fullint64Div2longWIN_recip
    push {r4-r11, lr}
    sub sp, sp, #D_SIZE
    str r0, [sp, #D_A]
    ldr r10, [sp, #D_LEN]
    teq r10, #0
    beq 9f
    // s = clz64(d), d <<= s (r2 = d lo, r3 = d hi)
    clz r4, r3
    cmp r3, #0
    clzeq r4, r2
    addeq r4, r4, #32
    str r4, [sp, #D_S]
    rsb r5, r4, #32
    sub r6, r4, #32
    lsl r7, r3, r4
    orr r7, r7, r2, lsr r5
    orr r7, r7, r2, lsl r6      // d1
    lsl r8, r2, r4              // d0
    str r7, [sp, #D_D1]
    str r8, [sp, #D_D0]
    mov r0, r7
    mov r1, r8
    bl .Linvert
    str r0, [sp, #D_V]

    // q = A << s: hi = xh<<s | xl>>(32-s) | xl<<(s-32) | yh>>(64-s), lo = xl<<s | yh>>(32-s) |
    // yh<<(s-32) | yl>>(64-s) for x = a[i], y = a[i+1] (register shifts of 32 and more give 0)
    ldr r0, [sp, #D_A]
    ldr r1, [sp, #D_Q]
    ldr r2, [sp, #D_LEN]
    ldr r4, [sp, #D_S]
    rsb r5, r4, #32
    sub r6, r4, #32
    rsb r7, r4, #64
    ldrd r10, r11, [r0], #8     // y = a[0]
    lsr r12, r11, r7            // the overflow: a[0] >> (64 - s)
    lsr lr, r11, r5
    orr lr, lr, r11, lsl r6
    orr lr, lr, r10, lsr r7
    str lr, [sp, #D_OLO]
    str r12, [sp, #D_OHI]
1:  mov r8, r10                 // x = y
    mov r9, r11
    subs r2, r2, #1
    moveq r10, #0               // below the last word: 0
    moveq r11, #0
    ldrdne r10, r11, [r0], #8
    lsl r12, r9, r4
    orr r12, r12, r8, lsr r5
    orr r12, r12, r8, lsl r6
    orr r12, r12, r11, lsr r7
    lsl lr, r8, r4
    orr lr, lr, r11, lsr r5
    orr lr, lr, r11, lsl r6
    orr lr, lr, r10, lsr r7
    str lr, [r1], #4
    str r12, [r1], #4
    teq r2, #0
    bne 1b

    ldr r4, [sp, #D_D0]
    ldr r5, [sp, #D_D1]
    ldr r6, [sp, #D_V]
    ldr r7, [sp, #D_OLO]
    ldr r8, [sp, #D_OHI]
    ldr r9, [sp, #D_Q]
    ldr r10, [sp, #D_LEN]
    bl .Ldivpass

    ldr r4, [sp, #D_S]          // the remainder >> s
    rsb r5, r4, #32
    sub r6, r4, #32
    lsr r0, r7, r4
    orr r0, r0, r8, lsl r5
    orr r0, r0, r8, lsr r6
    lsr r1, r8, r4
    b 8f
9:  mov r0, #0
    mov r1, #0
8:  ldr r2, [sp, #D_R]
    strd r0, r1, [r2]
    add sp, sp, #D_SIZE
    pop {r4-r11, pc}
END Fullint64Div2longWin

// -----------------------------------------------------------------------------
// ToDecString: repeated division by 10^19 (its v a constant), the digits right-aligned in
// resultBuf; *lenResult in: the buffer's length, out: the first digit's index. Each remainder
// (< 10^19) splits into c2 c1 c0 (one, nine, nine digits) by 2-by-1 divisions by 10^9 (as
// 4·10^9, normalized; its v a constant); a chunk's digits by the reciprocal of 10. The top
// chunk without leading zeros (one digit at least).
.equ T_VB, 0
.equ T_LEN, 4
.equ T_START, 8
.equ T_OUT, 12
.equ T_RB, 16
.equ T_SIZE, 24
.equ T_LENRES, T_SIZE + 36

// one digit of r0 (< 2^32) out at r11 (moving down), r0 = r0 / 10; r1-r3 used, r12 = 0xCCCCCCCD
.macro DIGIT
    umull r2, r1, r0, r12
    lsr r1, r1, #3              // c / 10
    add r3, r1, r1, lsl #2
    sub r2, r0, r3, lsl #1      // c - 10 (c / 10)
    add r2, r2, #48
    strb r2, [r11, #-1]!
    mov r0, r1
.endm

    .p2align 4
.Lemit9:                        // nine digits of r0
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    DIGIT
    bx lr
.Lemit_trim:                    // the digits of r0 up to its top nonzero one (one at least)
    DIGIT
    teq r0, #0
    bne .Lemit_trim
    bx lr

// (u1 u0) / 4·10^9 with u1 below it (2-by-1, Moller-Granlund; r9 = v = 0x12E0BE82, r10 = d):
// u1 = r1, u0 = r0 -> the quotient in r2, the remainder in r3
.macro DIV2BY1_E9
    umull r2, r3, r9, r1        // (q1 q0) = v u1 + (u1 u0)
    adds r2, r2, r0
    adc r3, r3, r1
    add r3, r3, #1
    mls r1, r3, r10, r0         // r = u0 - q1 d
    cmp r1, r2                  // r > q0: q1 - 1, r + d
    subhi r3, r3, #1
    addhi r1, r1, r10
    cmp r1, r10                 // r >= d (rare): q1 + 1, r - d
    addhs r3, r3, #1
    subhs r1, r1, r10
    mov r2, r3
    mov r3, r1
.endm

ENTRY Fullint64ToDecStringWin
ALIAS Fullint64ToDecStringWIN_recip
    push {r4-r11, lr}
    sub sp, sp, #T_SIZE
    str r1, [sp, #T_VB]
    str r3, [sp, #T_LEN]
    str r2, [sp, #T_RB]
    ldr r4, [sp, #T_LENRES]
    ldr r4, [r4]
    add r4, r2, r4
    str r4, [sp, #T_OUT]        // the output pointer: resultBuf + *lenResult
    mov r4, r1                  // valueBuf = value
    mov r5, r3
1:  subs r5, r5, #1
    blt 2f
    ldrd r6, r7, [r0], #8
    strd r6, r7, [r4], #8
    b 1b
2:  mov r4, #0
    bl .Lskip_zeros

.Lchunk:
    mov r7, #0                  // the remainder of this pass
    mov r8, #0
    ldr r4, [sp, #T_START]
    ldr r3, [sp, #T_LEN]
    subs r10, r3, r4
    beq 3f                      // nothing left to divide (a zero value)
    ldr r1, [sp, #T_VB]
    add r9, r1, r4, lsl #3
    movw r4, #0x0000            // 10^19 = d1 d0, v
    movt r4, #0x89E8
    movw r5, #0x2304
    movt r5, #0x8AC7
    movw r6, #0x94FB
    movt r6, #0xD83C
    bl .Ldivpass
    ldr r4, [sp, #T_START]
    bl .Lskip_zeros
3:  ldr r4, [sp, #T_START]
    ldr r3, [sp, #T_LEN]
    sub r6, r3, r4              // 0: the last chunk (the top one)
    movw r9, #0xBE82            // 4·10^9: v, d
    movt r9, #0x12E0
    movw r10, #0x2800
    movt r10, #0xEE6B
    lsr r1, r8, #30             // x << 2 = (x2 x1 x0)
    lsl r0, r8, #2
    orr r0, r0, r7, lsr #30
    DIV2BY1_E9
    mov r4, r2                  // y hi
    mov r1, r3
    lsl r0, r7, #2
    DIV2BY1_E9
    lsr r5, r3, #2              // c0 = x mod 10^9
    lsl r1, r4, #2              // y = x / 10^9 (< 10^10), y << 2 = (y2 y1)
    orr r1, r1, r2, lsr #30
    lsl r0, r2, #2
    DIV2BY1_E9
    lsr r4, r3, #2              // c1
    mov r7, r2                  // c2 (< 10)
    ldr r11, [sp, #T_OUT]
    movw r12, #0xCCCD
    movt r12, #0xCCCC
    teq r6, #0
    bne 4f
    teq r7, #0                  // the top chunk: its leading zeros dropped
    bne 4f
    mov r0, r5
    teq r4, #0
    beq 5f
    bl .Lemit9
    mov r0, r4
5:  bl .Lemit_trim
    b 6f
4:  mov r0, r5                  // 19 digits (the top chunk: c2 nonzero)
    bl .Lemit9
    mov r0, r4
    bl .Lemit9
    add r0, r7, #48
    strb r0, [r11, #-1]!
    str r11, [sp, #T_OUT]
    teq r6, #0
    bne .Lchunk
6:  ldr r2, [sp, #T_RB]
    sub r0, r11, r2
    ldr r1, [sp, #T_LENRES]
    str r0, [r1]
    add sp, sp, #T_SIZE
    pop {r4-r11, pc}

// start (r4) past the zero words of valueBuf -> T_START (the caller's frame: sp + 0 here, no
// push); r1-r3, r5, r6 used
.Lskip_zeros:
    ldr r1, [sp, #T_VB]
    ldr r3, [sp, #T_LEN]
1:  cmp r4, r3
    bhs 2f
    add r5, r1, r4, lsl #3
    ldr r6, [r5]
    ldr r2, [r5, #4]
    orrs r6, r6, r2
    bne 2f
    add r4, r4, #1
    b 1b
2:  str r4, [sp, #T_START]
    bx lr
END Fullint64ToDecStringWin

// -----------------------------------------------------------------------------
// FromDecString: the digits as base-10^19 blocks into valueBuf (the first block len mod 19
// digits, or 19), then base 2^64: each pass divides the blocks by 2^64 - the running remainder
// times 10^19 plus the next block, the 128-bit result's high word the block's quotient, its low
// word the remainder (four umaal a block); the last remainder a word of the result, from the end
// of resultBuf down. *lenResult in: resultBuf's length, out: the first word's index.
.equ P_VB, 0
.equ P_RB, 4
.equ P_SIZE, 8
.equ P_LENRES, P_SIZE + 36

ENTRY Fullint64FromDecStringWin
    push {r4-r11, lr}
    sub sp, sp, #P_SIZE
    str r1, [sp, #P_VB]
    str r2, [sp, #P_RB]
    mov r11, #0                 // blocks
    mov r10, #10
    mov r9, r3                  // the first block's digits: len mod 19 (19 for 0)
1:  cmp r9, #19
    subhi r9, r9, #19
    bhi 1b
    mov r8, r1                  // the next block's place
2:  cmp r3, #0
    ble 4f
    sub r3, r3, r9
    mov r4, #0                  // the block (r5 r4)
    mov r5, #0
3:  cmp r9, #9                  // a group of up to 9 digits in 32 bits (two instructions a digit),
    movhs r12, #9               // then block = block · 10^g + the group (umaal, mla)
    movlo r12, r9
    sub r9, r9, r12
    adr lr, .Lpow10
    ldr lr, [lr, r12, lsl #2]   // 10^g
    mov r6, #0
10: ldrb r7, [r0], #1
    sub r7, r7, #48
    add r6, r6, r6, lsl #2
    add r6, r7, r6, lsl #1
    subs r12, r12, #1
    bne 10b
    mov r7, #0
    umaal r6, r7, r4, lr
    mla r5, r5, lr, r7
    mov r4, r6
    teq r9, #0
    bne 3b
    strd r4, r5, [r8], #8
    add r11, r11, #1
    mov r9, #19
    b 2b

4:  ldr lr, [sp, #P_VB]
    ldr r12, [sp, #P_LENRES]
    ldr r12, [r12]              // the result's index (moves down)
    movw r6, #0x0000            // 10^19: Bl, Bh
    movt r6, #0x89E8
    movw r7, #0x2304
    movt r7, #0x8AC7
    mov r10, #0                 // start
5:  cmp r10, r11
    bhs 8f
    mov r4, #0                  // the remainder (r5 r4)
    mov r5, #0
    add r8, lr, r10, lsl #3
    sub r9, r11, r10
6:  ldrd r0, r1, [r8]           // the block (r1 r0)
    mov r2, #0
    umaal r0, r2, r4, r6        // w0
    umaal r1, r2, r4, r7        // rl·Bh + vh + c
    mov r3, #0
    umaal r1, r3, r5, r6        // + rh·Bl: w1
    umaal r2, r3, r5, r7        // rh·Bh + carries: (w3 w2)
    strd r2, r3, [r8], #8
    mov r4, r0
    mov r5, r1
    subs r9, r9, #1
    bne 6b
    sub r12, r12, #1
    ldr r0, [sp, #P_RB]
    add r0, r0, r12, lsl #3
    strd r4, r5, [r0]
7:  cmp r10, r11                // start past the zero blocks
    bhs 5b
    add r8, lr, r10, lsl #3
    ldrd r0, r1, [r8]
    orrs r0, r0, r1
    bne 5b
    add r10, r10, #1
    b 7b
8:  ldr r0, [sp, #P_LENRES]
    str r12, [r0]
    add sp, sp, #P_SIZE
    pop {r4-r11, pc}

    .p2align 2
.Lpow10:
    .word 1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000
END Fullint64FromDecStringWin
