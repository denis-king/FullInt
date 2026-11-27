// =============================================================================
//  ARMv7: And / Or / Xor (the contracts of x64 Logic.asm, kernels.h):
//    int Fullint64AndWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB,
//                        int lenR, int signs)                  (Or, Xor the same)
//  r = a op b over lenR words (>= max(lenA, lenB) + 1), right-aligned; signs bit 0 / 1: a / b
//  negative - the operands as two's complement (magnitude ^ mask + carry), the result back to a
//  magnitude the same way; returns 1 when the result is negative.
//  Both non-negative (the usual case): word by word, the longer operand's top words (Or / Xor)
//  or zeros (And), then zeros. Otherwise digit by digit with the three carry chains.
// =============================================================================

    .include "abi.inc"

.equ G_SIZE, 0
.equ A_LENB, 36
.equ A_LENR, 40
.equ A_SIGNS, 44

// \op: and / orr / eor; \keep: 1 - the longer operand's words survive (Or, Xor), 0 - not (And)
.macro LOGIC name, op, keep
ENTRY \name
    push {r4-r11, lr}
    ldr r4, [sp, #A_LENB]
    ldr r5, [sp, #A_LENR]
    ldr r6, [sp, #A_SIGNS]
    add r0, r0, r3, lsl #3      // the ends
    add r1, r1, r4, lsl #3
    add r2, r2, r5, lsl #3
    teq r6, #0
    bne 5f
    // non-negative: n = min(lenA, lenB) words of a op b, then the longer one's rest, then 0
    cmp r3, r4
    movlo r7, r3
    movhs r7, r4
    sub r5, r5, r7              // r's words left after the common part
    teq r7, #0
    beq 2f
1:  ldrd r8, r9, [r0, #-8]!
    ldrd r10, r11, [r1, #-8]!
    \op r8, r8, r10
    \op r9, r9, r11
    strd r8, r9, [r2, #-8]!
    subs r7, r7, #1
    bne 1b
2:  mov r10, #0
    mov r11, #0
.if \keep
    cmp r3, r4                  // the longer operand's remaining words: a's or b's
    subhs r7, r3, r4
    sublo r7, r4, r3
    movlo r0, r1
    sub r5, r5, r7
    teq r7, #0
    beq 4f
3:  ldrd r8, r9, [r0, #-8]!
    strd r8, r9, [r2, #-8]!
    subs r7, r7, #1
    bne 3b
.endif
4:  teq r5, #0                  // the words above: 0
    beq 9f
    strd r10, r11, [r2, #-8]!
    subs r5, r5, #1
    bne 4b
9:  mov r0, #0
    pop {r4-r11, pc}

    // a negative operand: two's complement digit by digit (lenR words, 2 digits each)
5:  and r7, r6, #1
    rsb r7, r7, #0              // maskA
    lsr r8, r6, #1
    and r8, r8, #1
    rsb r8, r8, #0              // maskB
    \op r9, r7, r8              // maskR
    and r10, r7, #1             // carryA (cA)
    and r11, r8, #1             // cB
    and r12, r9, #1             // cR
    push {r9}                   // (maskR for the result)
6:  // a's word (0 past its top), b's word, as digits
    subs r3, r3, #1
    movlt r4, #0
    movlt r6, #0
    ldrdge r4, r5, [r0, #-8]!   // (r5 a's hi)
    movlt r5, #0
    ldr lr, [sp, #4 + A_LENB]   // b's words left: kept on the stack
    subs lr, lr, #1
    str lr, [sp, #4 + A_LENB]
    movlt r6, #0
    movlt lr, #0
    ldrge r6, [r1, #-8]!
    ldrge lr, [r1, #4]
    // the low digits: ta = (a0 ^ mA) + cA, tb = (b0 ^ mB) + cB, w = op ^ mR + cR
    eor r4, r4, r7
    adds r4, r4, r10
    movcc r10, #0
    movcs r10, #1
    eor r6, r6, r8
    adds r6, r6, r11
    movcc r11, #0
    movcs r11, #1
    \op r4, r4, r6
    eor r4, r4, r9
    adds r4, r4, r12
    movcc r12, #0
    movcs r12, #1
    // the high digits
    eor r5, r5, r7
    adds r5, r5, r10
    movcc r10, #0
    movcs r10, #1
    eor lr, lr, r8
    adds lr, lr, r11
    movcc r11, #0
    movcs r11, #1
    \op r5, r5, lr
    eor r5, r5, r9
    adds r5, r5, r12
    movcc r12, #0
    movcs r12, #1
    strd r4, r5, [r2, #-8]!
    ldr lr, [sp, #4 + A_LENR]
    subs lr, lr, #1
    str lr, [sp, #4 + A_LENR]
    bne 6b
    pop {r9}
    and r0, r9, #1
    pop {r4-r11, pc}
END \name
.endm

    LOGIC Fullint64AndWIN, and, 0
    LOGIC Fullint64OrWIN, orr, 1
    LOGIC Fullint64XorWIN, eor, 1
