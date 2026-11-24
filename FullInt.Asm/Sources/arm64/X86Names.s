    .include "abi.inc"

// =============================================================================
//  The x86 kernel versions' names (BMI2 / ADX / AVX2) the .NET 64-bit core imports, each a jump
//  to the AArch64 kernel with the same contract. The core calls them only when Fullint64CpuFeatures
//  reports those bits (Cpu.s: never on AArch64), but .NET binds a SuppressGCTransition import
//  when its caller is compiled: a name missing from the library fails the caller even on the
//  branch not taken. (The x86 FFT / NTT kernels stay out: the core binds them only when it uses
//  them.)
// =============================================================================

.macro SAME name, base
ENTRY \name
    TAILCALL \base
END \name
.endm

    SAME Fullint64MultWIN_adx, Fullint64MultWIN
    SAME Fullint64SqrWIN_adx, Fullint64SqrWIN
    SAME Fullint64MulLoWIN_adx, Fullint64MulLoWIN
    SAME Fullint64AndWIN_avx2, Fullint64AndWIN
    SAME Fullint64OrWIN_avx2, Fullint64OrWIN
    SAME Fullint64XorWIN_avx2, Fullint64XorWIN
    SAME Fullint64ShlWIN_avx2, Fullint64ShlWIN
    SAME Fullint64ShrWIN_avx2, Fullint64ShrWIN
    SAME Fullint64CompareWIN_avx2, Fullint64CompareWIN
    SAME Fullint64DivWIN_recipadx, Fullint64DivWIN_recip
    SAME Fullint64DivQWIN_recipadx, Fullint64DivQWIN_recip
