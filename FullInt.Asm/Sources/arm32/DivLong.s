// =============================================================================
//  ARMv7: the long division by a reciprocal (the contracts of x64 DivRecip.asm, kernels.h):
//    void Fullint64DivWIN_recip(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r,
//                               int lenA, int lenB, uint64_t* work)
//      lenA > lenB >= 2, b[0] != 0; q: lenA - lenB + 1 words, r: lenB + 2 words (the remainder
//      in r[2 .. lenB+1], r[0] = r[1] = 0); work: lenA + 1 words (the contract says
//      lenA + lenB + 2). q may be a. U is in work, D in r until the remainder goes there.
//    uint64_t Fullint64DivQWIN_recip(a, b, q, r, lenA, lenB, work)
//      the quotient alone, approximately: Q' ~ floor(A·β / B) within 1 (β = 2^64), lenA - lenB + 2
//      words (the quotient and a guard word below it); r unused, work: lenA + 2 words, lenB >= 3.
//      Returns 0; or 1 - saturated: the caller divides exactly (also for lenB > 256 here: D goes
//      on the stack, 2 KB).
//
//  Knuth's algorithm D in 32-bit digits (b = 2^32), the digits little-endian in work: U = A << s
//  (2·lenA + 1 digits; DivQ: [A, 0] << s, 2·lenA + 3), D = B << s (nd digits, its top one
//  normalized). Each quotient digit by a 3-by-2 step on U's top three digits with the reciprocal
//  v of D's top two (Moller, Granlund 2011; Div.s), then U -= q·D on the nd - 2 digits below them
//  (umaal and a borrow) and the step's remainder less the borrow out; a borrow past it (rare):
//  q - 1, D added back. U's top two digits equal to D's (rare): q = b - 1, the full
//  multiply-subtract, D added back while the result is negative. The remainder: U's low nd
//  digits >> s.
//  DivQ (as GMP's divappr): the pass of a step on D's top min(nd, steps left + 4) digits only -
//  the digits below are owed to quotient digits under the guard word. The left-out error can
//  bring U's top two digits up to D's: saturated (the caller's exact division).
// =============================================================================

    .include "abi.inc"
    .fpu vfpv3-d16              // the reciprocal's first guess (armhf has VFPv3)

.equ L_U, 0             // U (work)
.equ L_D, 4             // D (in r; DivQ: on the stack, past the frame)
.equ L_NDB, 8           // nd · 4
.equ L_J, 12            // the quotient digit being found
.equ L_Q, 16            // q
.equ L_LQ, 20           // q's words
.equ L_V, 24
.equ L_S, 28
.equ L_A, 32
.equ L_B, 36
.equ L_R, 40
.equ L_SIZE, 48
.equ DQ_SPACE, 2048     // DivQ: D (up to 512 digits) after the frame

// the digits of n words (big-endian, from \end down) << s into \dst ascending; r4 = s,
// r5 = 32 - s; out: the digit shifted out on top in r0. \end, \dst, \n moved; r0-r3 used.
.macro SHIFT_DIGITS end, dst, n
    mov r0, #0                  // the digit below
1:  ldrd r2, r3, [\end, #-8]!
    lsl r1, r2, r4
    orr r1, r1, r0, lsr r5
    str r1, [\dst], #4
    lsl r1, r3, r4
    orr r1, r1, r2, lsr r5
    str r1, [\dst], #4
    mov r0, r3
    subs \n, \n, #1
    bne 1b
    lsr r0, r0, r5
.endm

// the 3-by-2 step (u2 = r8, u1 = r7, u0 = \u0; d0 = r4, d1 = r5, v = r6): Div.s's
.macro DIV3BY2 u0, qout
    umull r0, r1, r6, r8
    adds r0, r0, r7
    adc r1, r1, r8
    mls r8, r1, r5, r7
    umull r2, r3, r4, r1
    subs r7, \u0, r2
    sbc r8, r8, r3
    subs r7, r7, r4
    sbc r8, r8, r5
    add r1, r1, #1
    cmp r8, r0
    mvnhs r2, #0
    movlo r2, #0
    and r3, r2, r4
    adds r7, r7, r3
    and r3, r2, r5
    adc r8, r8, r3
    add \qout, r1, r2
    cmp r8, r5
    cmpeq r7, r4
    blo .Lok\@
    add \qout, \qout, #1
    subs r7, r7, r4
    sbc r8, r8, r5
.Lok\@:
.endm

// the span of a step's pass: lr = the digits (K + \extra), r10 = &U[j + nd - K], r6 =
// &D[nd - K]; K = nd, DivQ: min(nd, j + 5) (j + 1 steps left). r11 = D; r1-r3, r9 used.
.macro SPAN appr, extra
    ldr r9, [sp, #L_U]
    ldr r1, [sp, #L_J]
    add r9, r9, r1, lsl #2      // &U[j]
    ldr r3, [sp, #L_NDB]
    lsr r3, r3, #2              // nd
.if \appr
    ldr r2, [sp, #L_J]
    add r2, r2, #5
    cmp r2, r3
    movhi r2, r3                // K
.else
    mov r2, r3
.endif
    sub r1, r3, r2              // nd - K
    add r10, r9, r1, lsl #2
    add r6, r11, r1, lsl #2
    add lr, r2, #\extra
.endm

// U[r10 ..] -= q (r12) · D[r6 ..] over lr >= 1 digits from the low end; out: r0 = the borrow
// word, r10 past the digits. On the complement: ~U + q·D = (b^m - 1) - (U - q·D) + C·b^m, so
// U - q·D = ~(~U + q·D) less C·b^m - an add-multiply whose carry word C is the borrow: one umaal
// a digit, the carry its only chain (no flags). Two digits a pass (ldm / stm); an odd count: one
// digit first. r1-r4 used.
.macro SUBMUL
    mov r0, #0
    tst lr, #1
    beq 1f
    ldr r1, [r6], #4
    ldr r3, [r10]
    mvn r3, r3
    umaal r3, r0, r12, r1
    mvn r3, r3
    str r3, [r10], #4
    subs lr, lr, #1
    beq 2f
1:  ldmia r6!, {r1, r2}
    ldmia r10, {r3, r4}
    mvn r3, r3
    umaal r3, r0, r12, r1
    mvn r4, r4
    umaal r4, r0, r12, r2
    mvn r3, r3
    mvn r4, r4
    stmia r10!, {r3, r4}
    subs lr, lr, #2
    bne 1b
2:
.endm

.macro DIVLONG appr
    push {r4-r11, lr}
.if \appr
    sub sp, sp, #DQ_SPACE
.endif
    sub sp, sp, #L_SIZE
    str r0, [sp, #L_A]
    str r1, [sp, #L_B]
    str r2, [sp, #L_Q]
    str r3, [sp, #L_R]
    ldr r9, [sp, #(L_SIZE + DQ_SPACE * \appr + 36)]     // lenA
    ldr r10, [sp, #(L_SIZE + DQ_SPACE * \appr + 40)]    // lenB
.if \appr
    cmp r10, #(DQ_SPACE / 8)
    bhi .Lsat\appr
.endif
    ldr r11, [sp, #(L_SIZE + DQ_SPACE * \appr + 44)]    // work
    str r11, [sp, #L_U]
    sub r0, r9, r10
    add r0, r0, #(1 + \appr)
    str r0, [sp, #L_LQ]
    // s: the leading zeros of b's top nonzero digit; nd = 2 lenB (or 2 lenB - 1: a zero top digit)
    ldr r0, [r1, #4]
    ldr r2, [r1]
    lsl r3, r10, #1
    cmp r0, #0
    clzne r4, r0
    clzeq r4, r2
    subeq r3, r3, #1
    str r4, [sp, #L_S]
    lsl r3, r3, #2
    str r3, [sp, #L_NDB]
    rsb r5, r4, #32
    // U = A << s (DivQ: a zero word below A): its digits and the one shifted out
    mov r7, r11
.if \appr
    mov r0, #0
    str r0, [r7], #4
    str r0, [r7], #4
.endif
    ldr r6, [sp, #L_A]
    add r6, r6, r9, lsl #3
    mov r8, r9
    SHIFT_DIGITS r6, r7, r8
    str r0, [r7], #4
.if \appr
    add r7, sp, #L_SIZE
.else
    ldr r7, [sp, #L_R]          // D in r (2 lenB + 4 digits)
.endif
    str r7, [sp, #L_D]
    ldr r6, [sp, #L_B]          // D = B << s (its digit shifted out is 0)
    add r6, r6, r10, lsl #3
    mov r8, r10
    SHIFT_DIGITS r6, r7, r8
    ldr r0, [sp, #L_Q]          // q = 0
    ldr r1, [sp, #L_LQ]
    mov r2, #0
    mov r3, #0
2:  strd r2, r3, [r0], #8
    subs r1, r1, #1
    bne 2b
    ldr r11, [sp, #L_D]         // d1 d0, v
    ldr r3, [sp, #L_NDB]
    add r0, r11, r3
    ldr r1, [r0, #-8]
    ldr r0, [r0, #-4]
    bl .Linvert2
    str r0, [sp, #L_V]
    ldr r9, [sp, #(L_SIZE + DQ_SPACE * \appr + 36)]     // j from nu - nd - 1 down to 0
    lsl r9, r9, #1
.if \appr
    add r9, r9, #2
.endif
    ldr r3, [sp, #L_NDB]
    sub r9, r9, r3, lsr #2
    str r9, [sp, #L_J]

.Lstep\appr:
    ldr r11, [sp, #L_D]
    ldr r3, [sp, #L_NDB]
    add r12, r11, r3
    ldr r4, [r12, #-8]          // d0
    ldr r5, [r12, #-4]          // d1
    ldr r6, [sp, #L_V]
    ldr r9, [sp, #L_U]
    ldr r10, [sp, #L_J]
    add r9, r9, r10, lsl #2     // &U[j]
    add r12, r9, r3             // &U[j + nd]
    ldr r8, [r12]               // u2
    ldr r7, [r12, #-4]          // u1
    ldr lr, [r12, #-8]          // u0
    cmp r8, r5
    cmpeq r7, r4
.if \appr
    bhs .Lsat\appr              // the left-out error reached D: divide exactly
.else
    beq .Lequal\appr
.endif
    DIV3BY2 lr, r12             // q in r12, the remainder (n1 n0) in (r8 r7)

    SPAN \appr, -2              // U[.. j + nd - 3] -= q · D[.. nd - 3]
    SUBMUL
    subs r7, r7, r0             // n0 - borrow, n1 - its borrow
    sbcs r8, r8, #0
    str r7, [r10]
    str r8, [r10, #4]
    mov r1, #0                  // U[j + nd]: consumed (the remainder reads U[nd] at the end)
    str r1, [r10, #8]
    bcs .Lstore\appr
    sub r12, r12, #1            // negative (rare): q - 1, D added back, n1 += d1 + the carry
    SPAN \appr, -1
    cmn r0, #0                  // C = 0
4:  ldr r2, [r6], #4
    ldr r3, [r10]
    adcs r3, r3, r2
    str r3, [r10], #4
    sub lr, lr, #1
    teq lr, #0
    bne 4b
    adc r8, r8, r5
    str r8, [r10]
    b .Lstore\appr

.if \appr == 0
.Lequal\appr:                   // q = b - 1: U[j .. j + nd] -= q · D, D added back while negative
    mvn r12, #0
    SPAN 0, 0
    SUBMUL
    ldr r8, [r10]               // the top digit less the borrow
    subs r8, r8, r0
    str r8, [r10]
    bcs .Lstore\appr
6:  sub r12, r12, #1            // add D back until the top digit carries out
    SPAN 0, 0
    cmn r0, #0
7:  ldr r2, [r6], #4
    ldr r3, [r10]
    adcs r3, r3, r2
    str r3, [r10], #4
    sub lr, lr, #1
    teq lr, #0
    bne 7b
    ldr r8, [r10]
    adcs r8, r8, #0
    str r8, [r10]
    bcc 6b
.endif

.Lstore\appr:                   // q's digit j: word Lq - 1 - j/2, half j&1
    ldr r10, [sp, #L_J]
    ldr r0, [sp, #L_Q]
    ldr r1, [sp, #L_LQ]
    sub r1, r1, #1
    sub r1, r1, r10, lsr #1
    add r0, r0, r1, lsl #3
    and r1, r10, #1
    str r12, [r0, r1, lsl #2]
    subs r10, r10, #1
    str r10, [sp, #L_J]
    bge .Lstep\appr

.if \appr
    mov r0, #0                  // Q' within 1
    b .Lexit\appr
.Lsat\appr:
    mov r0, #1
.Lexit\appr:
    mov r1, #0
    add sp, sp, #L_SIZE
    add sp, sp, #DQ_SPACE
    pop {r4-r11, pc}
.else
    // r: 0, 0, then the remainder U[0 .. nd-1] >> s (U[nd] is 0 now) as lenB words
    ldr r0, [sp, #L_R]
    ldr r10, [sp, #(L_SIZE + 40)]
    mov r2, #0
    mov r3, #0
    strd r2, r3, [r0], #16
    strd r2, r3, [r0, #-8]
    add r0, r0, r10, lsl #3     // the end of r
    ldr r4, [sp, #L_S]
    rsb r5, r4, #32
    ldr r9, [sp, #L_U]
    ldr r6, [r9], #4            // U[0]
8:  ldr r7, [r9], #4            // U[k+1], U[k+2]
    ldr r8, [r9], #4
    subs r10, r10, #1
    moveq r8, #0                // the top word: nothing above it
    lsr r2, r6, r4              // lo = U[k] >> s | U[k+1] << (32 - s)
    orr r2, r2, r7, lsl r5
    lsr r3, r7, r4              // hi = U[k+1] >> s | U[k+2] << (32 - s)
    orr r3, r3, r8, lsl r5
    strd r2, r3, [r0, #-8]!
    mov r6, r8
    bne 8b
    add sp, sp, #L_SIZE
    pop {r4-r11, pc}
.endif
.endm

ENTRY Fullint64DivWIN_recip
    DIVLONG 0
END Fullint64DivWIN_recip

ENTRY Fullint64DivQWIN_recip
    DIVLONG 1
END Fullint64DivQWIN_recip

// v of (d1 = r0, d1 normalized; d0 = r1) -> r0 (Div.s's .Linvert; a copy: local labels)
    .p2align 4
.Linvert2:
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
    mul r2, r0, r3
    adds r2, r2, r1
    bcc 3f
    sub r3, r3, #1
    cmp r2, r0
    blo 2f
    sub r3, r3, #1
    sub r2, r2, r0
2:  sub r2, r2, r0
3:  umull r12, r4, r1, r3
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
