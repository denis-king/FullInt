    .include "abi.inc"
    .include "arith.inc"

// =============================================================================
//  Binary to decimal (AArch64), the contracts of x64 ToDecString.asm / ToDecStringRecip.asm /
//  ToDecStringFrac.asm.
//
//  void Fullint64ToDecStringWin(value, valueBuf, resultBuf, int lenValue, int* lenResult)
//  (and Fullint64ToDecStringWIN_recip: the same kernel - AArch64 divides by the reciprocal
//  anyway, it has no 128 / 64 division)
//    value copied to valueBuf (lenValue words), then divided by 10^19 again and again (one
//    STEP_2BY1 a word: 10^19 has its top bit set, no normalization, the reciprocal a constant),
//    each remainder 19 digits right-aligned in resultBuf; *lenResult in: the buffer's length,
//    out: the first digit's index. A chunk below the top one: H = r / 10^10 and L = r mod 10^10
//    as two chains of digits at once (9 and 10); the top chunk the same when it has more than
//    10 digits, its leading zeros trimmed after; up to 10 digits (and a buffer running out):
//    digit by digit.
//
//  Registers: x1 the value's words (moving up as its top words go), x2 resultBuf, x3 the
//  value's length, x4 lenResult, x5 the position, x6 = 10^19, x7 its reciprocal, x8 = the
//  reciprocal of 10 (x / 10 = umulh(x, x8) >> 3 for any 64-bit x), x9 the remainder, x10 the
//  words left in a pass, x11 the word pointer, x12 the quotient word, x13-x17 scratch.
// =============================================================================

// x = 10^19 / its 2-by-1 reciprocal floor((2^128 - 1) / 10^19) - 2^64 / ceil(2^67 / 10)
.macro CONST_1E19 x
    movz \x, #0x0000
    movk \x, #0x89E8, lsl #16
    movk \x, #0x2304, lsl #32
    movk \x, #0x8AC7, lsl #48
.endm
.macro CONST_V1E19 x
    movz \x, #0xC34A
    movk \x, #0x6D2A, lsl #16
    movk \x, #0x94FB, lsl #32
    movk \x, #0xD83C, lsl #48
.endm
.macro CONST_INV10 x
    mov \x, #0xCCCCCCCCCCCCCCCC
    add \x, \x, #1
.endm

// r (< 10^19) as H = r / 10^10 and L = r mod 10^10, 19 digits: L's ten end one byte before p,
// H's nine before them (p moved down 19); inv10 the reciprocal of 10; hw / lw: h / l as w registers
.macro DIGITS19 r, p, inv10, h, l, ph, t, k, hw, lw
    lsr \h, \r, #10                     // r / 10^10 = ((r >> 10) · ceil(2^78 / 5^10)) >> 78
    movz \k, #0xF6EB
    movk \k, #0x675E, lsl #16
    movk \k, #0xF37F, lsl #32
    movk \k, #0x6D, lsl #48
    umulh \h, \h, \k
    lsr \h, \h, #14
    movz \k, #0xE400
    movk \k, #0x540B, lsl #16
    movk \k, #0x2, lsl #32              // 10^10
    msub \l, \h, \k, \r                 // L
    sub \ph, \p, #10
    mov \k, #10
    .rept 9
    umulh \t, \l, \inv10                // L: two chains of digits, independent
    lsr \t, \t, #3
    msub \l, \t, \k, \l
    add \l, \l, #0x30
    strb \lw, [\p, #-1]!
    mov \l, \t
    umulh \t, \h, \inv10                // H
    lsr \t, \t, #3
    msub \h, \t, \k, \h
    add \h, \h, #0x30
    strb \hw, [\ph, #-1]!
    mov \h, \t
    .endr
    add \l, \l, #0x30                   // L's top digit
    strb \lw, [\p, #-1]!
    mov \p, \ph
.endm

// the top chunk? (the value zero after the pass: no words, or one zero word) -> top, else -> more
.macro IS_TOP top, more
    cmp x3, #1
    b.hi \more
    b.lo \top
    ldr x13, [x1]
    cbnz x13, \more
.endm

ENTRY Fullint64ToDecStringWin
fullint_arm64_todec:
    sxtw x3, w3
    mov x10, x3                         // the value to valueBuf
    mov x11, x1
1:  ldr x13, [x0], #8
    str x13, [x11], #8
    subs x10, x10, #1
    b.ne 1b
    ldrsw x5, [x4]                      // the buffer's length
    CONST_1E19 x6
    CONST_V1E19 x7
    CONST_INV10 x8
2:  mov x9, xzr                         // a pass: the value / 10^19, the remainder in x9
    mov x10, x3
    mov x11, x1
    cbz x10, 5f
3:  ldr x13, [x11]
    STEP_2BY1 x12, x9, x13, x6, x7, x14, x15
    str x12, [x11], #8
    cbnz x12, 4f
    cmp x10, x3
    b.ne 4f
    sub x3, x3, #1                      // a zero top word: the value one word shorter
4:  subs x10, x10, #1
    b.ne 3b
5:  sub x1, x11, x3, lsl #3             // the value's first word
    cmp x5, #19
    b.lt 20f                            // the buffer running out: digit by digit
    IS_TOP 6f, 7f
6:  movz x13, #0xE400                   // the top chunk with up to 10 digits: digit by digit
    movk x13, #0x540B, lsl #16
    movk x13, #0x2, lsl #32
    cmp x9, x13
    b.lo 20f
7:  add x16, x2, x5                     // one past the chunk's lowest digit
    DIGITS19 x9, x16, x8, x13, x14, x15, x12, x17, w13, w14
    sub x5, x5, #19
    IS_TOP 8f, 2b
8:  ldrb w13, [x2, x5]                  // the top chunk: its leading zeros (at most 8)
    cmp w13, #0x30
    b.ne 30f
    add x5, x5, #1
    b 8b

20: mov x16, #19                        // digit by digit
    mov x17, #10
21: sub x16, x16, #1
    subs x5, x5, #1
    b.mi 30f
    umulh x13, x9, x8
    lsr x13, x13, #3                    // r / 10
    msub x14, x13, x17, x9
    add w14, w14, #0x30
    strb w14, [x2, x5]
    mov x9, x13
    cbnz x9, 21b
    IS_TOP 30f, 22f
22: mov w14, #0x30                      // a lower chunk: its leading zeros
23: cmp x16, #0
    b.le 2b
    subs x5, x5, #1
    b.mi 30f
    strb w14, [x2, x5]
    sub x16, x16, #1
    b 23b
30: str w5, [x4]                        // the first digit's index
    ret
END Fullint64ToDecStringWin

ENTRY Fullint64ToDecStringWIN_recip
    b fullint_arm64_todec
END Fullint64ToDecStringWIN_recip

// =============================================================================
//  void Fullint64ToDecFracWIN(uint64_t* f, int64 L, int64 m, uint8_t* out)
//  Knuth's fraction method (TAOCP vol. 2, 4.4; x64 ToDecStringFrac.asm has the error bound):
//  f (L words, top first, overwritten) times 10^19 m times, each integer part 19 ASCII digits
//  at out (19·m bytes, leading zeros included). After a pass f loses its lowest word and the
//  new lowest is raised by one (f stays an upper bound within what the digits allow).
//
//  Registers: x0 f, x1 the active words, x2 chunks left, x3 out, x6 = 10^19, x8 the reciprocal
//  of 10, x9 the carry (then the digits), x10 the words left, x11 the word pointer, x12-x17
//  scratch.
// =============================================================================
ENTRY Fullint64ToDecFracWIN
    CONST_1E19 x6
    CONST_INV10 x8
1:  mov x9, xzr                         // f = f · 10^19 from the lowest word up
    mov x10, x1
    add x11, x0, x1, lsl #3
2:  ldr x12, [x11, #-8]!
    mul x13, x12, x6
    umulh x14, x12, x6
    adds x13, x13, x9
    adc x9, x14, xzr
    str x13, [x11]
    subs x10, x10, #1
    b.ne 2b
    cmp x1, #1                          // f loses its lowest word, the new lowest + 1
    b.eq 4f
    sub x1, x1, #1
    add x11, x0, x1, lsl #3
3:  ldr x12, [x11, #-8]!
    adds x12, x12, #1
    str x12, [x11]
    b.cc 4f
    cmp x11, x0
    b.ne 3b
4:  add x16, x3, #19                    // x9: the next 19 digits
    DIGITS19 x9, x16, x8, x13, x14, x15, x12, x17, w13, w14
    add x3, x3, #19
    subs x2, x2, #1
    b.ne 1b
    ret
END Fullint64ToDecFracWIN
