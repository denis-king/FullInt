// =============================================================================
//  ARMv7: the schoolbook product (the contract of x64 Mult.asm, kernels.h):
//    uint64_t Fullint64MultWIN(const uint64_t* a, const uint64_t* b, uint64_t* r,
//                              int lenA, int lenB, int lenR)
//  lenR >= lenA + lenB: r = a·b right-aligned, the words above it zero; a smaller lenR: the
//  product's low lenR words. r is neither a nor b. Returns 0.
//
//  In 32-bit digits with umaal (RdHi:RdLo = Rn·Rm + RdLo + RdHi, never overflows): r is zeroed,
//  then one pass per word of the shorter operand - its two digits b0, b1 at once (two rows, two
//  carry chains c0 / c1) over the words of the longer one:
//      r[k]   += a[k]·b0 + a[k-1]·b1      (digit k of the pass, from the low end)
//  four umaal per word of a (its 64 x 64 product), one load / store of r per word. Each pass
//  leaves two digits of carry: the next word up, not yet written (r was zeroed) - stored as is.
//  The passes skip a zero word of b. A truncated product: a pass stops at r's top word and its
//  carry is dropped.
//
//  lenB, lenR on the stack: [sp, #0] / [sp, #4] at the entry.
// =============================================================================

    .include "abi.inc"

// the frame (below the saved r4-r11, lr)
.equ F_AEND, 0          // a + 8·lenA: the end of the longer operand
.equ F_LA, 4            // its words
.equ F_PB, 8            // the end of the current word of b (moves down a word a pass)
.equ F_PASSES, 12       // passes left: min(lenB, lenR) at the start
.equ F_REND, 16         // the end of the current pass in r (moves down a word a pass)
.equ F_ROOM, 20         // r's words from the pass's end up: lenR - j
.equ F_SIZE, 24
.equ A_LENB, F_SIZE + 36
.equ A_LENR, F_SIZE + 40

ENTRY Fullint64MultWIN
    push {r4-r11, lr}
    sub sp, sp, #F_SIZE
    ldr r4, [sp, #A_LENB]
    ldr r5, [sp, #A_LENR]
    // r = 0
    mov r6, #0
    mov r7, #0
    movs r9, r5
    beq 2f
    mov r10, r2
1:  strd r6, r7, [r10], #8
    subs r9, r9, #1
    bne 1b
2:  // the longer operand in r0 / r3
    cmp r3, r4
    bhs 3f
    mov r9, r0
    mov r0, r1
    mov r1, r9
    mov r9, r3
    mov r3, r4
    mov r4, r9
3:  teq r4, #0                  // an empty operand or result: 0
    beq .Ldone
    teq r5, #0
    beq .Ldone
    add r9, r0, r3, lsl #3
    str r9, [sp, #F_AEND]
    str r3, [sp, #F_LA]
    add r9, r1, r4, lsl #3
    str r9, [sp, #F_PB]
    cmp r4, r5
    movhi r4, r5                // no pass starts above r's top word
    str r4, [sp, #F_PASSES]
    add r9, r2, r5, lsl #3
    str r9, [sp, #F_REND]
    str r5, [sp, #F_ROOM]

.Lpass:
    ldr r1, [sp, #F_PB]
    ldrd r8, r9, [r1, #-8]!     // b0 (lo), b1 (hi) of b's next word up
    str r1, [sp, #F_PB]
    orrs r3, r8, r9
    beq .Lnext
    ldr r0, [sp, #F_AEND]
    ldr r1, [sp, #F_REND]
    ldr r2, [sp, #F_LA]
    ldr r3, [sp, #F_ROOM]
    cmp r2, r3
    movhi r2, r3                // the words of a this pass: min(lenA, room)
    mov r10, #0                 // c0
    mov r11, #0                 // c1
    mov r12, #0                 // a's digit below the current word's: none yet

    .p2align 4
.Lrow:
    ldrd r4, r5, [r0, #-8]!     // a's word: x0 (r4), x1 (r5)
    ldrd r6, r7, [r1, #-8]!     // r's word: t0, t1
    umaal r6, r10, r4, r8       // t0 += x0·b0 + c0
    umaal r6, r11, r12, r9      // t0 += x_-1·b1 + c1
    umaal r7, r10, r5, r8       // t1 += x1·b0 + c0
    umaal r7, r11, r4, r9       // t1 += x0·b1 + c1
    strd r6, r7, [r1]
    mov r12, r5
    subs r2, r2, #1
    bne .Lrow

    // the pass's carry: c0 + x_top·b1 + c1 (< 2^64), the next word up - inside r only when
    // the row covered all of a (room > lenA)
    ldr r2, [sp, #F_LA]
    ldr r3, [sp, #F_ROOM]
    cmp r3, r2
    bls .Lnext
    mov r6, r10
    umaal r6, r11, r12, r9
    mov r7, r11
    strd r6, r7, [r1, #-8]

.Lnext:
    ldr r1, [sp, #F_REND]
    sub r1, r1, #8
    str r1, [sp, #F_REND]
    ldr r3, [sp, #F_ROOM]
    sub r3, r3, #1
    str r3, [sp, #F_ROOM]
    ldr r4, [sp, #F_PASSES]
    subs r4, r4, #1
    str r4, [sp, #F_PASSES]
    bne .Lpass

.Ldone:
    mov r0, #0
    mov r1, #0
    add sp, sp, #F_SIZE
    pop {r4-r11, pc}
END Fullint64MultWIN
