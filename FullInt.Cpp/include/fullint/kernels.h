// FullInt asm kernels (FullInt.Asm/Sources/x64, x86), linked statically.
//
// The same entry points the .NET core calls through DllImport (FullIntCoreWindowsX64.cs),
// with the same contracts:
//   - limbs are 64-bit words, index 0 = most significant word;
//   - lengths are word counts;
//   - Div expects a divisor without leading zero words; quotient: lenA - lenB + 1 words,
//     remainder: lenB + 2 words, the value right-aligned;
//   - ToDecString / FromDecString: lenResult in = buffer length, out = start index.
//
// Windows only for now: the ...WIN entry points (Win64 ABI on x64, cdecl on x86).
#pragma once

#include <cstdint>

#if !defined(_WIN32)
#error "FullInt kernels: only Windows (x64, x86) is wired up so far"
#endif

extern "C" {

uint64_t Fullint64AddWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR);
int      Fullint64SubWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR);
uint64_t Fullint64MultWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR);
uint64_t Fullint64DivWin(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r, int lenA, int lenB);
uint64_t Fullint64Div2longWin(const uint64_t* a, uint64_t b, uint64_t* q, uint64_t* r, int lenA);
int      Fullint64CompareWIN(const uint64_t* a, const uint64_t* b, int lenA, int lenB);
void     Fullint64ToDecStringWin(const uint64_t* value, uint64_t* valueBuf, uint8_t* resultBuf, int lenValue, int* lenResult);
void     Fullint64FromDecStringWin(const uint8_t* value, uint64_t* valueBuf, uint64_t* resultBuf, int lenValue, int* lenResult);

int  Fullint64AndWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
int  Fullint64OrWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
int  Fullint64XorWIN(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
void Fullint64ShlWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift);
void Fullint64ShrWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift);

// CPU features for picking kernel versions (Cpu.asm): x64 bit 0 BMI2, bit 1 ADX, bit 2 AVX2
// (OS-enabled); x86 bit 4 SSE2
int Fullint64CpuFeatures(void);

// kernel versions for newer CPUs (same contracts as the baseline ones above)
#if defined(_M_X64) || defined(__x86_64__)

void Fullint64MultWIN_adx(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR);   // BMI2 + ADX
int  Fullint64AndWIN_avx2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);   // AVX2
int  Fullint64OrWIN_avx2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
int  Fullint64XorWIN_avx2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
void Fullint64ShlWIN_avx2(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift);   // AVX2
void Fullint64ShrWIN_avx2(const uint64_t* a, uint64_t* r, int lenA, int lenR, int shift);
int  Fullint64CompareWIN_avx2(const uint64_t* a, const uint64_t* b, int lenA, int lenB);    // AVX2
#else
int  Fullint64AndWIN_sse2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);   // x86 SSE2
int  Fullint64OrWIN_sse2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
int  Fullint64XorWIN_sse2(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR, int signs);
#endif

// division by a precomputed reciprocal (DivRecip.asm, x64 and x86): any CPU. Div: lenA > lenB >= 2,
// work = lenA + lenB + 2 words of scratch; otherwise the contracts of the baseline Div / Div2long.
void Fullint64Div2longWIN_recip(const uint64_t* a, uint64_t b, uint64_t* q, uint64_t* r, int lenA);
void Fullint64DivWIN_recip(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r, int lenA, int lenB, uint64_t* work);
// ToDecString with the division by 10^19 through its reciprocal (ToDecStringRecip.asm, x64 and x86)
void Fullint64ToDecStringWIN_recip(const uint64_t* value, uint64_t* valueBuf, uint8_t* resultBuf, int lenValue, int* lenResult);

}
