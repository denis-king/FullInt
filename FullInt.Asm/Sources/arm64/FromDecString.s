    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Decimal to binary (AArch64), the contract of x64 FromDecString.asm:
//    void Fullint64FromDecStringWin(value, valueBuf, resultBuf, int lenValue, int* lenResult)
//  The digits (lenValue >= 1 ASCII bytes) as base-10^19 chunks in valueBuf (the first chunk
//  lenValue mod 19 digits, or 19), then base 2^64: each pass multiplies the remainder by 10^19
//  word by word (the chunks divided by 2^64), the last remainder a binary word, from resultBuf's
//  end down; *lenResult in: the buffer's length, out: the first word's index (0 when the buffer
//  ran out).
//  A full chunk, 19 digits = 8 + 8 + 3: eight at once (SWAR - the bytes to digits, then pairs,
//  fours, eights by multiply and shift); the first, shorter chunk digit by digit.
//
//  Registers: x0 the digits, x1 valueBuf (then the value's first word), x2 resultBuf, x3 the
//  digits left (then the value's length), x4 lenResult, x5 the chunks / the position, x6 =
//  10^19, x7 '0' in every byte, x8 = 100, x9 = 10000, x10 = 10^11, x11 = 1000, x12 the digits of
//  this chunk, x13-x17 scratch.
// =============================================================================

// r = the value of the 8 decimal digits at [p] (the first one the most significant), p += 8
.macro EIGHT_DIGITS r, p, t
    ldr \r, [\p], #8
    sub \r, \r, x7                      // byte i: digit i
    lsr \t, \r, #8
    add \r, \r, \r, lsl #2
    add \r, \t, \r, lsl #1              // 10 · d_i + d_(i+1)
    and \r, \r, #0x00FF00FF00FF00FF     // the even bytes: pairs
    lsr \t, \r, #16
    madd \r, \r, x8, \t
    and \r, \r, #0x0000FFFF0000FFFF     // the even 16-bit lanes: fours
    lsr \t, \r, #32
    madd \r, \r, x9, \t
    and \r, \r, #0xFFFFFFFF             // eight
.endm

ENTRY Fullint64FromDecStringWin
    sxtw x3, w3
    movz x6, #0x0000                    // 10^19
    movk x6, #0x89E8, lsl #16
    movk x6, #0x2304, lsl #32
    movk x6, #0x8AC7, lsl #48
    mov x7, #0x3030303030303030
    mov x8, #100
    mov x9, #10000
    movz x10, #0xE800                   // 10^11
    movk x10, #0x4876, lsl #16
    movk x10, #0x17, lsl #32
    mov x11, #1000
    mov x13, #19
    udiv x12, x3, x13
    msub x12, x12, x13, x3              // the first chunk: lenValue mod 19 digits
    cbnz x12, 1f
    mov x12, #19
1:  mov x5, xzr                         // the chunks
2:  cmp x12, #19
    b.ne 3f
    EIGHT_DIGITS x13, x0, x14           // a full chunk
    EIGHT_DIGITS x15, x0, x14
    mul x13, x13, x10
    madd x13, x15, x11, x13
    ldrb w14, [x0]                      // the last three
    ldrb w15, [x0, #1]
    ldrb w16, [x0, #2]
    add x0, x0, #3
    sub w14, w14, #0x30
    sub w15, w15, #0x30
    sub w16, w16, #0x30
    mov w17, #10
    madd w14, w14, w17, w15
    madd w14, w14, w17, w16
    add x13, x13, x14
    b 5f
3:  mov x13, xzr                        // a shorter first chunk: digit by digit
    mov x17, x12
4:  ldrb w14, [x0], #1
    sub w14, w14, #0x30
    add x13, x13, x13, lsl #2
    add x13, x14, x13, lsl #1           // x · 10 + d
    subs x17, x17, #1
    b.ne 4b
5:  str x13, [x1, x5, lsl #3]
    add x5, x5, #1
    subs x3, x3, x12
    mov x12, #19
    b.gt 2b
    mov x3, x5                          // the value's length
    ldrsw x5, [x4]                      // the position
6:  mov x12, xzr                        // a pass: the value / 2^64, the remainder in x12
    mov x13, x3
    mov x14, x1
7:  cbz x13, 9f
    ldr x15, [x14]
    mul x16, x12, x6
    umulh x17, x12, x6
    adds x12, x16, x15                  // remainder · 10^19 + chunk = (quotient : remainder)
    adc x17, x17, xzr
    str x17, [x14], #8
    cbnz x17, 8f
    cmp x13, x3
    b.ne 8f
    sub x3, x3, #1                      // a zero top word: the value one word shorter
    add x1, x1, #8
8:  sub x13, x13, #1
    b 7b
9:  subs x5, x5, #1
    b.mi 11f
    str x12, [x2, x5, lsl #3]
    cmp x3, #1                          // the value zero: done
    b.hi 6b
    b.lo 10f
    ldr x15, [x1]
    cbnz x15, 6b
10: str w5, [x4]                        // the first word's index
    ret
11: str wzr, [x4]                       // the buffer ran out
    ret
END Fullint64FromDecStringWin
