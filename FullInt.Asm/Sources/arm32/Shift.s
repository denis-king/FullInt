// =============================================================================
//  ARMv7: the shifts (the contracts of x64 Shift.asm, kernels.h):
//    void Fullint64ShlWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift)
//      r >= lenA + shift / 64 + 1 words: zeros, a << (shift mod 64) (lenA + 1 words), then
//      shift / 64 zero words
//    void Fullint64ShrWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift)
//      r >= max(lenA - shift / 64, 1) words, right-aligned; r may be a (lenR = lenA): from the
//      low end up, the zeros last
//  A word pair (x above y) shifted by s = shift mod 64 is a funnel shift of three of its digits
//  by u = s mod 32 (a register shift by 32 gives 0, so u = 0 needs no branch); s < 32 and
//  s >= 32 pick the digits - two copies of each loop, four shifts a word:
//    (x:y) << s:  s < 32: (P1 P2 P3) = (xh xl yh), s >= 32: (xl yh yl);
//                 hi = P1<<u | P2>>(32-u), lo = P2<<u | P3>>(32-u)
//    (x:y) >> s:  s < 32: (Q1 Q2 Q3) = (yl yh xl), s >= 32: (yh xl xh);
//                 lo = Q1>>u | Q2<<(32-u), hi = Q2>>u | Q3<<(32-u)
// =============================================================================

    .include "abi.inc"

.equ A_SHIFT, 36

// the a << s words: r8 r9 = x (xl xh), r10 r11 = y (yl yh); r12 words, r2 a's words left
.macro SHL_WORDS p1, p2, p3
1:  subs r2, r2, #1             // y: a's next word, 0 past its last
    movlt r10, #0
    movlt r11, #0
    ldrdge r10, r11, [r0], #8
    lsl r3, \p1, r4             // hi
    orr r3, r3, \p2, lsr r5
    lsl lr, \p2, r4             // lo
    orr lr, lr, \p3, lsr r5
    str lr, [r1], #4
    str r3, [r1], #4
    mov r8, r10
    mov r9, r11
    subs r12, r12, #1
    bne 1b
.endm

ENTRY Fullint64ShlWIN
    push {r4-r11, lr}
    ldr r12, [sp, #A_SHIFT]
    lsr lr, r12, #6             // ws
    and r4, r12, #31            // u
    rsb r5, r4, #32
    and r6, r12, #32            // s >= 32
    add r12, r2, lr             // the zeros on top: lenR - (lenA + 1 + ws)
    add r12, r12, #1
    sub r12, r3, r12
    mov r10, #0
    mov r11, #0
1:  subs r12, r12, #1
    blt 2f
    strd r10, r11, [r1], #8
    b 1b
2:  mov r8, #0                  // x: the word above a[0]
    mov r9, #0
    add r12, r2, #1             // lenA + 1 words out
    teq r6, #0
    bne 3f
    SHL_WORDS r9, r8, r11
    b 4f
3:  SHL_WORDS r8, r11, r10
4:  ldr r12, [sp, #A_SHIFT]     // ws zero words below
    lsr r12, r12, #6
    mov r10, #0
    mov r11, #0
5:  subs r12, r12, #1
    blt 6f
    strd r10, r11, [r1], #8
    b 5b
6:  pop {r4-r11, pc}
END Fullint64ShlWIN

// the a >> s words from the low end: r10 r11 = y (yl yh, the current word), r8 r9 = x (the word
// above, 0 above a[0]); r12 words, r0 / r2 moving down
.macro SHR_WORDS q1, q2, q3
1:  cmp r12, #1
    movls r8, #0
    movls r9, #0
    ldrdhi r8, r9, [r0, #-8]!
    lsr r3, \q1, r4             // lo
    orr r3, r3, \q2, lsl r5
    lsr r10, \q2, r4            // hi
    orr r10, r10, \q3, lsl r5
    str r10, [r2, #-4]
    str r3, [r2, #-8]!
    mov r10, r8
    mov r11, r9
    subs r12, r12, #1
    bne 1b
.endm

ENTRY Fullint64ShrWIN
    push {r4-r11, lr}
    ldr r12, [sp, #A_SHIFT]
    lsr lr, r12, #6             // ws
    and r4, r12, #31            // u
    rsb r5, r4, #32
    and r6, r12, #32            // s >= 32
    subs r12, r2, lr            // the words out: lenA - ws
    ble 8f
    sub lr, r3, r12             // the zeros on top
    add r0, r0, r12, lsl #3     // a's words from count - 1 down, r's from its end
    add r2, r1, r3, lsl #3
    ldrd r10, r11, [r0, #-8]!   // y = a[count - 1]
    teq r6, #0
    bne 3f
    SHR_WORDS r10, r11, r8
    b 4f
3:  SHR_WORDS r11, r8, r9
4:  mov r10, #0                 // the zeros on top, last (in place: read already)
    mov r11, #0
5:  subs lr, lr, #1
    blt 9f
    strd r10, r11, [r1], #8
    b 5b
8:  mov r10, #0                 // everything shifted out: lenR zero words
    mov r11, #0
6:  subs r3, r3, #1
    blt 9f
    strd r10, r11, [r1], #8
    b 6b
9:  pop {r4-r11, pc}
END Fullint64ShrWIN
