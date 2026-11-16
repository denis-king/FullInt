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

// exact division for the Toom-Cook interpolation (ToomOps.asm, baseline instructions):
// q = a / d for a multiple of d, d = d'·2^t with d' odd and below 2^32; q may be a; returns 0
// when a is a multiple
uint64_t Fullint64DivExactWIN(const uint64_t* a, uint64_t* q, int len, uint64_t d);

// R = A² (SqrAdx.asm, BMI2 + ADX; x64 and x86): lenR >= 2·lenA, R != A, the words above
// 2·lenA zero
void Fullint64SqrWIN_adx(const uint64_t* a, uint64_t* r, int lenA, int lenR);

// the low half of a product (MulLoAdx.asm, BMI2 + ADX; x64 and x86): R = A·B mod β^n
// (n words, n <= lenA + lenB)
void Fullint64MulLoWIN_adx(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int n);

// number-theoretic transforms on 8 lanes of 32 bits (NttAvx2.asm, AVX2; x64 and x86; Ntt.cpp has the scheme)
void Fullint64NttDif1WIN_avx2(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p);
void Fullint64NttDif2WIN_avx2(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p);
void Fullint64NttDif3WIN_avx2(uint32_t* x, int64_t L, const uint32_t* w, const uint32_t* wq, uint32_t p);
void Fullint64NttDit1WIN_avx2(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p);
void Fullint64NttDit2WIN_avx2(uint32_t* x, int64_t L, int64_t h, const uint32_t* w, const uint32_t* wq, uint32_t p);
void Fullint64NttDit3WIN_avx2(uint32_t* x, int64_t L, const uint32_t* w, const uint32_t* wq, uint32_t p);
// the radix-3 level of a transform of 3M values; k: nine vectors of constants (Ntt.cpp)
void Fullint64NttR3DifWIN_avx2(uint32_t* x, int64_t M, const uint32_t* k, uint32_t p);
void Fullint64NttR3DitWIN_avx2(uint32_t* x, int64_t M, const uint32_t* k, uint32_t p);
void Fullint64NttPointWIN_avx2(uint32_t* x, const uint32_t* y, int64_t n, uint32_t p, uint32_t pinv);
void Fullint64NttScaleWIN_avx2(uint32_t* x, int64_t n, uint32_t p, uint32_t c, uint32_t cq);
void Fullint64NttLoadWIN_avx2(const uint64_t* a, int64_t la, uint32_t* x, int64_t L, uint32_t p);
uint64_t Fullint64NttCrtWIN(const uint32_t* x0, const uint32_t* x1, const uint32_t* x2, int64_t nd, uint64_t* r, int64_t len, const uint64_t* k);

// CPU features for picking kernel versions (Cpu.asm): bit 0 BMI2, bit 1 ADX, bit 2 AVX2
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
// the FFT butterfly halves mod 2^N + 1 (FftAdx.asm, BMI2 + ADX; n1 = N/64 + 1 words)
void Fullint64FftAddSubWIN_adx(uint64_t* u, uint64_t* v, int n1);
// DivRecip.asm's long division with the multiply-subtract pass on mulx / adcx / adox (BMI2 + ADX)
void Fullint64DivWIN_recipadx(const uint64_t* a, const uint64_t* b, uint64_t* q, uint64_t* r, int lenA, int lenB, uint64_t* work);
void Fullint64FftShlModWIN_bmi2(const uint64_t* x, uint64_t* dst, int n1, int s);
// R = A² without BMI2 / ADX (Sqr.asm, mul / adc), the same contract as Fullint64SqrWIN_adx
void Fullint64SqrWIN(const uint64_t* a, uint64_t* r, int lenA, int lenR);
#else
void Fullint64MultWIN_adx(const uint64_t* a, const uint64_t* b, uint64_t* r, int lenA, int lenB, int lenR);   // x86 BMI2 + ADX
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
