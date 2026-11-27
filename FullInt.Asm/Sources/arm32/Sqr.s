// =============================================================================
//  ARMv7: R = A² (the contract of x64 Sqr.asm, kernels.h):
//    void Fullint64SqrWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR)
//  lenR >= 2·lenA, r not a; the words of r above 2·lenA zero.
//
//  A² = 2·T + D in words (β = 2^64, positions from the low end):
//    T = sum over m < m' of a_m·a_m'·β^(m+m'): row m - Mult.s's pass with a_m's two digits as
//        the multiplier, over the words above it, into r from position 2m + 1; the row's carry
//        word (position m + lenA) is new: r was cleared;
//    r doubled (one adcs pass over 2·lenA words);
//    D = sum of a_m²·β^(2m): the square's four digits by umull / umaal (no flags), added with
//        one adcs chain.
//  About half the digit products of Mult(a, a).
// =============================================================================

    .include "abi.inc"

.equ S_AEND, 0
.equ S_REND, 4
.equ S_LEN, 8
.equ S_M, 12
.equ S_SIZE, 16

ENTRY Fullint64SqrWIN
    push {r4-r11, lr}
    sub sp, sp, #S_SIZE
    // r = 0
    mov r6, #0
    mov r7, #0
    movs r9, r3
    beq 2f
    mov r10, r1
1:  strd r6, r7, [r10], #8
    subs r9, r9, #1
    bne 1b
2:  teq r2, #0
    beq .Lsq_done
    add r0, r0, r2, lsl #3
    str r0, [sp, #S_AEND]
    add r1, r1, r3, lsl #3
    str r1, [sp, #S_REND]
    str r2, [sp, #S_LEN]
    mov r4, #0
    str r4, [sp, #S_M]

    // the rows: m = 0 .. lenA - 2
.Lsq_row:
    ldr r4, [sp, #S_M]
    ldr r2, [sp, #S_LEN]
    sub r2, r2, r4
    subs r2, r2, #1             // the words above a_m
    beq .Lsq_double
    ldr r0, [sp, #S_AEND]
    sub r0, r0, r4, lsl #3
    ldrd r8, r9, [r0, #-8]!     // a_m: b0, b1; r0 now at a_m (the next word up below it)
    ldr r1, [sp, #S_REND]
    sub r1, r1, r4, lsl #4
    sub r1, r1, #8              // the end of position 2m + 1
    mov r10, #0
    mov r11, #0
    mov r12, #0
    orrs r3, r8, r9
    beq .Lsq_next               // a zero word: nothing (its carry word stays 0)
3:  ldrd r4, r5, [r0, #-8]!
    ldrd r6, r7, [r1, #-8]!
    umaal r6, r10, r4, r8
    umaal r6, r11, r12, r9
    umaal r7, r10, r5, r8
    umaal r7, r11, r4, r9
    strd r6, r7, [r1]
    mov r12, r5
    subs r2, r2, #1
    bne 3b
    mov r6, r10                 // the row's carry word: position m + lenA (new)
    umaal r6, r11, r12, r9
    mov r7, r11
    strd r6, r7, [r1, #-8]
.Lsq_next:
    ldr r4, [sp, #S_M]
    add r4, r4, #1
    str r4, [sp, #S_M]
    b .Lsq_row

.Lsq_double:                    // r = 2·r over 2·lenA words (two a pass)
    ldr r1, [sp, #S_REND]
    ldr r2, [sp, #S_LEN]
    cmn r1, #0                  // C = 0
4:  ldmdb r1, {r4-r7}
    adcs r6, r6, r6
    adcs r7, r7, r7
    adcs r4, r4, r4
    adcs r5, r5, r5
    stmdb r1!, {r4-r7}
    sub r2, r2, #1
    teq r2, #0
    bne 4b

    // + a_m² at positions 2m, 2m + 1, one carry chain from the low end
    ldr r0, [sp, #S_AEND]
    ldr r1, [sp, #S_REND]
    ldr r2, [sp, #S_LEN]
    cmn r1, #0
5:  ldrd r8, r9, [r0, #-8]!     // lo, hi
    umull r10, r11, r8, r8      // lo²
    mov r12, #0
    umaal r11, r12, r8, r9      // + lo·hi at digit 1, twice
    mov lr, #0
    umaal r11, lr, r8, r9
    umaal r12, lr, r9, r9       // hi² + the carries: digits 2, 3
    ldmdb r1, {r4-r7}           // r4 r5: position 2m + 1, r6 r7: position 2m
    adcs r6, r6, r10
    adcs r7, r7, r11
    adcs r4, r4, r12
    adcs r5, r5, lr
    stmdb r1!, {r4-r7}
    sub r2, r2, #1
    teq r2, #0
    bne 5b
.Lsq_done:
    add sp, sp, #S_SIZE
    pop {r4-r11, pc}
END Fullint64SqrWIN
