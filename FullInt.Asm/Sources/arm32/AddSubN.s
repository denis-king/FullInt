// =============================================================================
//  ARMv7: the equal-length add / subtract (the contracts of x64 AddSubN.asm, kernels.h):
//    uint64_t Fullint64AddNWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
//    uint64_t Fullint64SubNWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
//    uint64_t Fullint64SubCmpNWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int64_t n)
//  r = a ± b on n words (r may be a or b), the carry / borrow returned. SubCmpN: |a - b| from
//  the first word where a and b differ, at its places in r (the words above untouched); returns
//  off (the zero words above the difference; n when a == b, nothing written) | (b > a) << 63.
//
//  n (an int64_t after three pointers) is on the stack. Two words (four 32-bit digits) a pass:
//  ldmdb / stmdb from the low end up, the carry in C across the passes - the loop counts with
//  sub (no flags) and teq #0 (Z only: C stays).
// =============================================================================

    .include "abi.inc"

// r0 / r1 / r2 = the ends (one past the lowest word) of x, y, r; r3 = the words (any count):
// r = x + y (.Laddn) or x - y (.Lsubn) from the low end up. Out: C = the carry (add) / C clear
// = a borrow (sub); r0-r2 moved down to x, y, r; r3-r11 used.
    .p2align 4
.Laddn:
    cmn r0, #0                  // C = 0: no carry in
    tst r3, #1
    beq 1f
    ldmdb r0!, {r4, r5}         // an odd count: the lowest word alone
    ldmdb r1!, {r6, r7}
    adcs r4, r4, r6
    adcs r5, r5, r7
    stmdb r2!, {r4, r5}
1:  lsr r3, r3, #1
    teq r3, #0
    beq 3f
2:  ldmdb r0!, {r4-r7}          // words i-1 (r4 lo, r5 hi) and i (r6, r7): i is the lower one
    ldmdb r1!, {r8-r11}
    adcs r6, r6, r10
    adcs r7, r7, r11
    adcs r4, r4, r8
    adcs r5, r5, r9
    stmdb r2!, {r4-r7}
    sub r3, r3, #1
    teq r3, #0
    bne 2b
3:  bx lr

    .p2align 4
.Lsubn:
    cmp r0, r0                  // C = 1: no borrow in
    tst r3, #1
    beq 1f
    ldmdb r0!, {r4, r5}
    ldmdb r1!, {r6, r7}
    sbcs r4, r4, r6
    sbcs r5, r5, r7
    stmdb r2!, {r4, r5}
1:  lsr r3, r3, #1
    teq r3, #0
    beq 3f
2:  ldmdb r0!, {r4-r7}
    ldmdb r1!, {r8-r11}
    sbcs r6, r6, r10
    sbcs r7, r7, r11
    sbcs r4, r4, r8
    sbcs r5, r5, r9
    stmdb r2!, {r4-r7}
    sub r3, r3, #1
    teq r3, #0
    bne 2b
3:  bx lr

ENTRY Fullint64AddNWIN
    push {r4-r11, lr}           // n at [sp, #36]
    ldr r3, [sp, #36]
    add r0, r0, r3, lsl #3
    add r1, r1, r3, lsl #3
    add r2, r2, r3, lsl #3
    bl .Laddn
    mov r0, #0
    adc r0, r0, #0
    mov r1, #0
    pop {r4-r11, pc}
END Fullint64AddNWIN

ENTRY Fullint64SubNWIN
    push {r4-r11, lr}
    ldr r3, [sp, #36]
    add r0, r0, r3, lsl #3
    add r1, r1, r3, lsl #3
    add r2, r2, r3, lsl #3
    bl .Lsubn
    movcc r0, #1                // C clear: a borrow
    movcs r0, #0
    mov r1, #0
    pop {r4-r11, pc}
END Fullint64SubNWIN

ENTRY Fullint64SubCmpNWIN
    push {r4-r11, lr}
    ldr r3, [sp, #36]           // n
    mov r12, #0                 // i: the first word where a and b differ
    mov r4, r0
    mov r5, r1
1:  cmp r12, r3
    beq 8f                      // a == b
    ldr r6, [r4, #4]            // the high halves first
    ldr r7, [r5, #4]
    cmp r6, r7
    bne 2f
    ldr r6, [r4], #8
    ldr r7, [r5], #8
    cmp r6, r7
    bne 2f
    add r12, r12, #1
    b 1b
2:  movlo r6, r0                // a's word below b's: |a - b| = b - a, the sign bit out
    movlo r0, r1
    movlo r1, r6
    orrlo r12, r12, #0x80000000
    add r0, r0, r3, lsl #3
    add r1, r1, r3, lsl #3
    add r2, r2, r3, lsl #3
    bic r6, r12, #0x80000000
    sub r3, r3, r6              // the words from i down: n - i
    bl .Lsubn                   // (no borrow out: the larger one first)
    ldr r3, [sp, #36]
    sub r3, r3, #1
    bic r0, r12, #0x80000000    // off = i, past the zero words of the difference (r2 = &r[i])
3:  cmp r0, r3
    bhs 4f
    ldrd r4, r5, [r2], #8
    orrs r4, r4, r5
    bne 4f
    add r0, r0, #1
    b 3b
4:  and r1, r12, #0x80000000
    pop {r4-r11, pc}
8:  mov r0, r3
    mov r1, #0
    pop {r4-r11, pc}
END Fullint64SubCmpNWIN

// -----------------------------------------------------------------------------
//  uint64_t Fullint64AddWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB,
//                           int lenR)
//  r = a + b over lenR words (the operands' missing top words 0); the carry out of r's top word.
//  The common words through .Laddn, the longer one's rest with the carry, then the carry word and
//  zeros.
ENTRY Fullint64AddWIN
    push {r4-r11, lr}
    ldr r4, [sp, #36]           // lenB
    ldr r12, [sp, #40]          // lenR
    cmp r3, r4                  // the longer operand in r0 / r3
    movlo r5, r0
    movlo r0, r1
    movlo r1, r5
    movlo r5, r3
    movlo r3, r4
    movlo r4, r5
    add r0, r0, r3, lsl #3      // the ends
    add r1, r1, r4, lsl #3
    add r2, r2, r12, lsl #3
    sub r3, r3, r4              // the longer one's own words
    cmp r4, r12                 // the common words: min(lenB, lenR)
    movhi r4, r12
    sub r12, r12, r4            // r's words left
    sub sp, sp, #8
    stmia sp, {r3, r12}
    mov r3, r4
    bl .Laddn                   // C: the carry
    mov r6, #0
    adc r6, r6, #0              // (kept across the compares below)
    ldmia sp, {r3, r12}
    add sp, sp, #8
    cmp r3, r12                 // the rest of the longer one: min(its words, r's left)
    movhi r3, r12
    sub r12, r12, r3
    cmp r6, #1                  // C = the carry again
    teq r3, #0
    beq 2f
1:  ldmdb r0!, {r4, r5}
    adcs r4, r4, #0
    adcs r5, r5, #0
    stmdb r2!, {r4, r5}
    sub r3, r3, #1
    teq r3, #0
    bne 1b
2:  mov r0, #0
    adc r0, r0, #0              // the carry
    mov r1, #0
    teq r12, #0
    popeq {r4-r11, pc}          // r full: the carry out
    mov r4, #0
    stmdb r2!, {r0, r4}         // the carry word, then zeros; 0 out
    mov r0, #0
3:  subs r12, r12, #1
    popeq {r4-r11, pc}
    stmdb r2!, {r0, r4}
    b 3b
END Fullint64AddWIN

//  int Fullint64SubWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB,
//                      int lenR)
//  |a - b| over lenR words, the sign of a - b returned (Compare first: the larger one less the
//  smaller); the smaller one's top words past the larger one's are zeros.
ENTRY Fullint64SubWIN
    push {r4-r11, lr}
    sub sp, sp, #16
    stmia sp, {r0-r3}           // a, b, r, lenA
    mov r2, r3                  // Compare(a, b, lenA, lenB)
    ldr r3, [sp, #(16 + 36)]
    bl Fullint64CompareWIN
    mov r11, r0                 // the sign
    ldmia sp, {r0-r3}
    ldr r4, [sp, #(16 + 36)]    // lenB
    ldr r12, [sp, #(16 + 40)]   // lenR
    add sp, sp, #16
    teq r11, #0
    beq 8f
    cmp r11, #0                 // b larger: swap
    movlt r5, r0
    movlt r0, r1
    movlt r1, r5
    movlt r5, r3
    movlt r3, r4
    movlt r4, r5
    add r0, r0, r3, lsl #3
    add r1, r1, r4, lsl #3
    add r2, r2, r12, lsl #3
    cmp r4, r3                  // the common words: min(lenL, lenG, lenR)
    movhi r4, r3
    cmp r4, r12
    movhi r4, r12
    sub r3, r3, r4              // the larger one's own words
    sub r12, r12, r4
    sub sp, sp, #16
    stmia sp, {r3, r11, r12}    // (.Lsubn takes r4-r11)
    mov r3, r4
    bl .Lsubn                   // C clear: a borrow (into the larger one's rest)
    mov r6, #0
    adc r6, r6, #0              // (kept across the compares below)
    ldmia sp, {r3, r11, r12}
    add sp, sp, #16
    cmp r3, r12
    movhi r3, r12
    sub r12, r12, r3
    cmp r6, #1                  // C = no borrow, again
    teq r3, #0
    beq 2f
1:  ldmdb r0!, {r4, r5}
    sbcs r4, r4, #0
    sbcs r5, r5, #0
    stmdb r2!, {r4, r5}
    sub r3, r3, #1
    teq r3, #0
    bne 1b
2:  mov r4, #0                  // zeros on top
    mov r5, #0
3:  subs r12, r12, #1
    blt 4f
    stmdb r2!, {r4, r5}
    b 3b
4:  mov r0, r11
    pop {r4-r11, pc}
8:  mov r4, #0                  // a == b: r = 0
    mov r5, #0
    add r2, r2, r12, lsl #3
    b 3b
END Fullint64SubWIN
