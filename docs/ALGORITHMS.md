# Algorithms

Notation: `n`, `m` are operand sizes in 64-bit limbs; `M(n)` is the cost of multiplying two n-limb numbers.

## Current implementation

The same algorithms run in the .NET library (C#, `FullInt.Core`) and the C++ one (`FullInt.Cpp/src`). The words inside are 64-bit, most significant first. The thresholds below are the x64 defaults in words; x86 and the narrower ARM64 cores (`ArmCpu.Narrow`, e.g. Neoverse-N1) have their own.

### Kernels (assembly, `FullInt.Asm/Sources`)

| Kernel | x64 baseline | x64 with BMI2 / ADX / AVX2 | x86 | ARM64 |
|---|---|---|---|---|
| Add, subtract | `adc` / `sbb` loops | | 32-bit port | `adcs` / `sbcs`, `ldp` / `stp` |
| Compare, AND / OR / XOR, shifts | scalar | AVX2 (`CompareAvx2`, `LogicAvx2`, `ShiftAvx2`) | scalar | scalar |
| Multiply basecase | product scanning (`Mult`) | `mulx` + `adcx` / `adox` (`MultAdx`) | 32-bit port | `mul` / `umulh` |
| Square, low half of a product | `Sqr` | `SqrAdx`, `MulLoAdx` | ✓ | ✓ |
| Divide by one word | `div` | precomputed reciprocal (`DivRecip`, Möller–Granlund) | `div` | reciprocal (no 128/64 `udiv`) |
| Long division | estimate + correction (`Div`) | reciprocal of the top words | ✓ | reciprocal |
| Decimal text, both ways | base 10¹⁹ (`ToDecString`, `FromDecString`), the fraction method for short numbers | | ✓ | ✓ |
| NTT butterflies | — | AVX2 (`NttAvx2`) | AVX2 | NEON (`NttNeon`) |
| Schönhage–Strassen butterflies | — | `FftAdx` | C# / C++ | C# / C++ |

The kernel set is picked at start-up with `cpuid`; `FULLINT_CPU=baseline` forces the baseline set. On .NET a call into a kernel costs about 12–16 ns, and the short calls skip the GC transition on .NET 8 (`SuppressGCTransition`). The shortest operations (one or two words, short compares) stay in C#.

### Multiplication

| Size (shorter operand) | Algorithm | Cost |
|---|---|---|
| 1 word | `Math.BigMul` in C# | O(1) |
| below 64 | schoolbook basecase in assembly; squaring has its own kernel up to 96 | O(n·m) |
| 64 – 255 | Karatsuba | O(n^1.585) |
| 256 – 511 | Toom-3 (points 0, 1, −1, −2, ∞; Bodrato's interpolation) | O(n^1.465) |
| 512 – 1023 | Toom-4 | O(n^1.404) |
| from 1024 | Toom-6 | O(n^1.338) |
| from 896 (when the AVX2 / NEON kernels exist) | **NTT**: 32-bit digits, three primes below 2³⁰, transform length 2^k or 3·2^k, Garner's CRT; products up to 2²¹ words | O(n log n) |
| from 8192 (no NTT kernels) | **Schönhage–Strassen**: convolution mod 2^N + 1, twiddles as shifts | O(n log n log log n) |
| unbalanced operands | the longer one is cut into pieces of the shorter one's length | |

Karatsuba and the Toom levels are written in the upper layer (C# / C++) over the assembly add, subtract, shift and exact-division kernels. Their scratch memory comes from `ArrayPool` (C#) or a thread-local buffer (C++).

### Division

| Case | Algorithm | Cost |
|---|---|---|
| divisor of one word | reciprocal per word (Möller–Granlund) | O(n) |
| short divisor or short quotient | long division in assembly: the digit estimated from the top words, at most a couple of corrections | O(n·m) |
| divisor and quotient both from 96 words | **Burnikel–Ziegler** (block size 48), over the fast multiplication | O(M(n)·log n) |
| divisor from 2048 words | **Newton reciprocal**, then the quotient in blocks (Barrett style): about 2.5–4.5 products in all | O(M(n)) |
| quotient only (`/` without the remainder) | an approximate quotient from the top words, corrected by one multiplication | saves the remainder's product |

### Decimal text

| Direction | Algorithm | Cost |
|---|---|---|
| `ToString` | divide and conquer by cached powers 10^(19·2^k); leaves of 32–48 words in assembly (base 10¹⁹, or the fraction method) | O(M(n)·log n) |
| `Parse` | divide and conquer: halves multiplied by the cached powers; leaves of up to 912 digits in assembly | O(M(n)·log n) |

### Other operations

| Operation | How |
|---|---|
| Bit operations on negative numbers | the two's complement computed on the fly, as `BigInteger` |
| Shifts | word moves plus `shld` / `shrd` (x64), `extr` (ARM64), AVX2 for long numbers |
| `Pow` | binary exponentiation, squaring through the squaring path |
| Conversion to `double` / `float` | the top 63 bits plus a sticky bit, one rounding (correctly rounded) |

## Planned algorithms

| Operation | Algorithm |
|---|---|
| GCD | Binary GCD for small sizes, **Lehmer's** algorithm (double digit) for medium, half-GCD for very large |
| Modular inverse | Extended Lehmer GCD |
| ModPow | Montgomery form + **sliding-window** exponentiation (window 4–6 bits depending on the exponent's size) |
| Integer square root, n-th root | Newton iteration with an initial estimate from the top bits |
| Primality | Trial division by small primes, then **Miller–Rabin** with random bases, optionally Baillie–PSW |
| Per-CPU thresholds | A table by CPU family plus a one-time tuner that writes a profile keyed by the CPU model |
| Parallel products | Above about 100k bits, the top-level Toom subproducts or the NTT transforms on several threads |

## Correctness strategy

- **Reference oracles.** The .NET suite checks every operation against `System.Numerics.BigInteger`; the C++ suite against GMP.
- **Both cores and every kernel set.** The .NET suite runs on the native and the managed core (`FULLINT_FORCE_MANAGED=1`); the C++ suite with the CPU's kernels and with `FULLINT_CPU=baseline`; on ARM64 every assembly kernel is also checked word for word against its portable C++ version (`fullint_arm64_kernels`).
- **Edge cases, not only uniform random words.** The generators include:
  - all-ones words, single set bits, powers of two ± 1;
  - divisors whose top word is `0x8000…0000` or `0xFFFF…FFFF`;
  - quotient digits that hit `2⁶⁴ − 1`;
  - lengths around every threshold.
- **Identities**: `(a·b)/b == a`, `a == q·b + r` with `|r| < |b|`, `Parse(ToString(a)) == a`.
- **Platforms**: Windows x64 and x86, Linux x64 (Docker), Linux ARM64 (a phone and a cloud machine).
- **Planned**: a fixed-seed CI run on every commit and a long randomized run on a schedule.

## References

- D. Knuth, *The Art of Computer Programming*, vol. 2, §4.3 "Multiple-Precision Arithmetic"
- R. Brent, P. Zimmermann, *Modern Computer Arithmetic* (free PDF), the most practical reference for every algorithm above
- N. Möller, T. Granlund, "Improved division by invariant integers", IEEE TC 2011
- C. Burnikel, J. Ziegler, "Fast Recursive Division", MPI-I-98-1-022
- M. Bodrato, "Towards Optimal Toom-Cook Multiplication for Univariate and Multivariate Polynomials"
- GMP manual, chapter "Algorithms", and the `mpn/x86_64` and `mpn/arm64` sources
