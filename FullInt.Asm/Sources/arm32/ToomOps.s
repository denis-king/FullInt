// =============================================================================
//  ARMv7: the exact division for the Toom-Cook interpolation (the contract of x64 ToomOps.asm):
//    uint64_t Fullint64DivExactWIN(const uint64_t* a, uint64_t* q, int len, uint64_t d)
//  q = a / d for an a known to be a multiple of d; d = d'·2^t with d' odd, below 2^32; q may be
//  a. Returns the last c of the Hensel division: 0 when a was a multiple (the same value as the
//  64-bit kernels: the steps are on 64-bit words).
//  q = a >> t (Fullint64ShrWIN, in place), then from the low end per word, inv = d'^-1 mod 2^64:
//    q_i = (x_i - c)·inv mod 2^64,  c' = hi64(q_i·d') + (x_i < c)
//  inv: d'^-1 mod 2^32 by Newton (5 bits from (3d') xor 2, doubled three times), lifted to
//  64 bits: d'·inv32 = 1 + h·2^32 -> inv = inv32 + (-h·inv32 mod 2^32)·2^32.
// =============================================================================

    .include "abi.inc"

ENTRY Fullint64DivExactWIN
    push {r4-r11, lr}           // d at [sp, #36] (lo), [sp, #40] (hi)
    ldr r4, [sp, #36]
    ldr r5, [sp, #40]
    mov r9, r1                  // q
    mov r10, r2                 // len
    // t = the trailing zeros of d, d' = d >> t (below 2^32)
    rbit r6, r4
    clz r6, r6                  // ctz(lo) (32 for 0)
    cmp r4, #0
    rbiteq r7, r5
    clzeq r7, r7
    addeq r6, r7, #32
    rsb r7, r6, #32
    sub r8, r6, #32
    lsr r11, r4, r6             // d' = lo >> t | hi << (32 - t) | hi >> (t - 32)
    orr r11, r11, r5, lsl r7
    orr r11, r11, r5, lsr r8
    // q = a >> t (lenR = len: the words stay in place)
    teq r10, #0
    beq 9f
    sub sp, sp, #8
    str r6, [sp]
    mov r3, r10
    bl Fullint64ShrWIN
    add sp, sp, #8
    // inv32: x = (3d') ^ 2, x = x (2 - d' x) three times
    add r3, r11, r11, lsl #1
    eor r3, r3, #2
    .rept 3
    mul r4, r11, r3
    rsb r4, r4, #2
    mul r3, r3, r4
    .endr
    umull r4, r12, r11, r3      // h = hi(d'·inv32)
    mul r4, r12, r3
    rsb r4, r4, #0              // inv hi = -h·inv32
    // the words from the low end: r3 inv lo, r4 inv hi, r5 c, r11 d'
    mov r5, #0
    add r0, r9, r10, lsl #3
1:  ldrd r6, r7, [r0, #-8]!     // x
    subs r6, r6, r5             // x - c, the borrow
    sbcs r7, r7, #0
    movcc r12, #1
    movcs r12, #0
    umull r8, r9, r6, r3        // q = (x - c)·inv mod 2^64
    mla r9, r6, r4, r9
    mla r9, r7, r3, r9
    strd r8, r9, [r0]
    umull r6, r7, r8, r11       // c = hi64(q·d') + the borrow
    umull r8, r1, r9, r11
    adds r8, r8, r7
    adc r5, r1, r12
    subs r10, r10, #1
    bne 1b
    mov r0, r5
    mov r1, #0
    pop {r4-r11, pc}
9:  mov r0, #0
    mov r1, #0
    pop {r4-r11, pc}
END Fullint64DivExactWIN
