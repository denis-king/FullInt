    .include "abi.inc"

// =============================================================================
//  int Fullint64CpuFeatures(void): the x86 feature bits (bit 0 BMI2, 1 ADX, 2 AVX2) the .NET
//  core asks its native library for at start-up (x64 Cpu.asm). AArch64 has none of them: 0. The
//  .NET core then takes the baseline entries, and the division by reciprocal (its bit 3, which
//  it sets itself) - on AArch64 every division divides by a reciprocal anyway (Div.s).
// =============================================================================
ENTRY Fullint64CpuFeatures
    mov w0, #0
    ret
END Fullint64CpuFeatures
