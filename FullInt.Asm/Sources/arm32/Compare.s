// =============================================================================
//  ARMv7: the magnitude compares (the contracts of x64 Compare.asm / CompareN.asm, kernels.h):
//    int Fullint64CompareWIN(const uint64_t* a, const uint64_t* b, int lenA, int lenB)
//      the top zero words skipped, then the lengths, then the words from the top: -1 / 0 / 1
//    int Fullint64CmpNWIN(const uint64_t* a, const uint64_t* b, int64_t n)
//      equal lengths, the words from the top
// =============================================================================

    .include "abi.inc"

ENTRY Fullint64CompareWIN
    push {r4-r7, lr}
1:  cmp r2, #0                  // a's top zero words
    beq 2f
    ldrd r4, r5, [r0]
    orrs r4, r4, r5
    bne 2f
    add r0, r0, #8
    sub r2, r2, #1
    b 1b
2:  cmp r3, #0                  // b's
    beq 3f
    ldrd r4, r5, [r1]
    orrs r4, r4, r5
    bne 3f
    add r1, r1, #8
    sub r3, r3, #1
    b 2b
3:  cmp r2, r3
    bne 7f
    b .Lcmpn
END Fullint64CompareWIN

ENTRY Fullint64CmpNWIN
    push {r4-r7, lr}            // n in r2 (r3: its high half, 0)
.Lcmpn:
    teq r2, #0
    beq 6f
4:  ldrd r4, r5, [r0], #8
    ldrd r6, r7, [r1], #8
    cmp r5, r7
    cmpeq r4, r6
    bne 7f
    subs r2, r2, #1
    bne 4b
6:  mov r0, #0
    pop {r4-r7, pc}
7:  movhi r0, #1                // (lengths or words) unsigned: above / below
    mvnlo r0, #0
    pop {r4-r7, pc}
END Fullint64CmpNWIN
