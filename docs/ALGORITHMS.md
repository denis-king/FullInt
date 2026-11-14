# Algorithms

Notation: `n`, `m` are operand sizes in 64-bit limbs; `M(n)` is the cost of multiplying two n-limb numbers.

## Current implementation

| Operation | Where | Algorithm | Cost |
|---|---|---|---|
| Add / Sub | `Add.asm`, `Sub.asm` | Limb loop from LSW to MSW with `adc`/`sbb`. The carry is saved in `al` with `setc` and restored with `shr al,1`. Sub compares magnitudes first and swaps operands so the result is non-negative. | O(n) |
| Compare | `Compare.asm` | Skip leading zeros, compare lengths, then limbs from MSW | O(n) |
| Multiply | `Mult.asm` | **Product scanning (column-wise) schoolbook.** For each result limb, sum all `a[i]·b[j]` with `i + j = k` into a 192-bit accumulator (`r13:r14:r15`) using `mul` + `adc`. The inner loop always runs over all of `b` and range-checks `i`. | O(n·(n+m)) iterations, about 2× the necessary work |
| Divide by 1 limb | `Div2Long.asm` | Native `div r64` (128/64 → 64) per limb, from MSW | O(n), but `div` costs 30–90 cycles |
| Divide | `Div.asm` | Long division with a quotient-digit estimate: the top two words are normalized on every step with `bsr` + `shld`, `q_min = U / (D+1)` and `q_max = U / D` are computed, then interpolated using the next divisor word. The digit is applied with a multiply-subtract and **one** ±1 correction. | O(n·m), ⚠️ the estimate can be off by more than 1, which gives wrong digits |
| To decimal string | `ToDecString.asm` | Repeated division of the whole number by 10¹⁹, each remainder producing 19 digits | O(n²) |
| From decimal string | `FromDecString.asm` | Parse 19-digit chunks, then convert radix 10¹⁹ → 2⁶⁴ by repeated division | O(n²) |
| Power | `FullInt64Engine.pow` | Right-to-left binary exponentiation. The exponent bits come from `ToString(2)`. | O(log e) multiplications |
| Radix mode conversion | `ConvertToBase` | Repeated division by 10¹⁹ (bin → dec) or by 2⁶⁴ written in radix 10¹⁹ (dec → bin) | O(n²) |

### Why the schoolbook multiply loses to `BigInteger` today

1. **Wasted iterations.** About half the inner iterations of `Mult.asm` are out of range and do nothing but branch.
2. **Algorithmic threshold.** `BigInteger` switches to Karatsuba above a few dozen 32-bit words, so for large inputs it wins on complexity, whatever the word size.
3. **Overhead per call.** Two allocations per result (`new ulong[]` + `DeleteZeros().ToArray()`), span pinning, and a P/Invoke transition. For numbers under about 8 limbs this costs more than the arithmetic.
4. **No benchmark harness.** `Stopwatch` inside unit tests includes JIT time, GC and test framework noise.

## Planned algorithms

### Multiplication ladder

| Size (limbs, to be tuned) | Algorithm | Complexity | Notes |
|---|---|---|---|
| 1 | `mul` / `Math.BigMul` | O(1) | Stay in managed code, no P/Invoke |
| 2 – ~32 | **Schoolbook, operand scanning**: `mul_1` for the first row, then `addmul_1` for each remaining row | O(n·m) | Inner loop has no branches. On x64, the BMI2+ADX variant uses `mulx` + `adcx`/`adox` (two independent carry chains), about 1.5–2× faster. Unroll 4×. |
| ~32 – ~200 | **Karatsuba** ✅ | O(n^1.585) | Three half-size products: `z0 = a0·b0`, `z2 = a1·b1`, `z1 = (a0+a1)(b0+b1) − z0 − z2`. Recurse until the threshold. Uses a scratch buffer of about 2n limbs from `ArrayPool`. |
| ~200 – ~3000 | **Toom-Cook 3** (Toom-3) | O(n^1.465) | Five products at evaluation points {0, 1, −1, 2, ∞}, Bodrato's interpolation sequence. |
| > ~3000 (≈ 200k bits) | **NTT** (three-prime number-theoretic transform + CRT) or **Schönhage–Strassen** | O(n log n log log n) | Needs 64-bit modular multiplication with primes p < 2⁶³, Montgomery or Shoup reduction. |
| unbalanced (m ≪ n) | Split the longer operand into m-sized chunks and add the shifted partial products | | Avoids wasting Karatsuba on zero padding |
| `a·a` | **Dedicated squaring** | ~0.6–0.7 × M(n) | Compute only the off-diagonal products, double them, add the diagonal |

**Implemented now** (`FullInt.Core/Karatsuba.cs`):
- 1 × 1 limb: managed `FullHelper.BigMul`, no P/Invoke.
- Basecase: the author's column (product-scanning) asm `Mult`, with per-column bounds and a 2× unrolled inner loop.
- Karatsuba from **80 limbs** in the shorter operand, in C# over the asm basecase, add and sub. It uses the subtractive form `z1 = z0 + z2 − (a1−a0)(b1−b0)`, so every intermediate stays at m or 2m+1 limbs. Scratch is about 8n limbs from `ArrayPool`. Unbalanced operands are cut into chunks, as in the table.
- The threshold is high because each C# → asm call costs about 40–50 ns and one Karatsuba level makes five add/sub calls. To bring it down to about 30: a cheaper P/Invoke transition, or the per-level add/sub fused into one asm kernel. Tuning: `dotnet run -c Release --project FullInt.Benchmarks -- kara`. Numbers: [benchmarks/2026-11-14-karatsuba.md](benchmarks/2026-11-14-karatsuba.md).

Above roughly 100k bits, the three top-level Karatsuba or Toom subproducts can run in parallel with `Parallel.Invoke`, at the cost of a separate scratch buffer per branch.

### Division

| Algorithm | When | Complexity |
|---|---|---|
| `divrem_1` with **precomputed reciprocal** (Möller–Granlund, "Improved division by invariant integers") | divisor of 1 limb | O(n), about 3–5× faster than `div` per limb. Required on ARM64, which has no 128/64 divide instruction. |
| **Knuth Algorithm D** (TAOCP vol. 2, §4.3.1) | divisor of 2 limbs or more | O(n·m). Normalize once (shift so the divisor's top bit is set). Estimate `q̂` from the top two dividend limbs and the top divisor limb, refine with the second divisor limb (at most two corrections), then `submul_1`. If the result goes negative, add back once. This makes every quotient digit provably correct. |
| **Burnikel–Ziegler** recursive division | m > ~60 limbs | O(M(n)·log n). Reuses Karatsuba and Toom. |
| **Newton reciprocal** + multiply | very large, or repeated division by the same divisor | O(M(n)) |
| **Barrett reduction** / **Montgomery multiplication** | modular arithmetic with a fixed modulus | O(M(n)) per reduction. Montgomery avoids division entirely. |

### Radix conversion (string ↔ binary)

| Algorithm | Complexity |
|---|---|
| Current: repeated division by 10¹⁹ | O(n²) |
| **Divide and conquer**: precompute `10^(19·2^k)`, split the number by the largest power, recurse on both halves (parsing works symmetrically with multiply-and-add) | O(M(n)·log n) |

For a million-digit number this is the difference between minutes and well under a second. The table of powers can be cached and shared.

### Other planned operations

| Operation | Algorithm |
|---|---|
| GCD | Binary GCD for small sizes, **Lehmer's** algorithm (double-digit) for medium, half-GCD for very large |
| Modular inverse | Extended Lehmer GCD |
| ModPow | Montgomery form + **sliding-window** exponentiation (window 4–6 bits depending on exponent size) |
| Integer square root, n-th root | Newton iteration with an initial estimate from the top bits (`Math.Sqrt` on the leading 53 bits) |
| Primality | Trial division by small primes, then **Miller–Rabin** with random bases, optionally Baillie–PSW |
| Bitwise ops on negative numbers | Two's-complement view computed on the fly, as `BigInteger` does |
| Shifts | `shld`/`shrd` loops (x64), `extr` (ARM64) |

## Correctness strategy

- **Reference oracle.** Every operation is checked against `System.Numerics.BigInteger` on random inputs.
- **Edge-case generators**, not just uniform random bytes: all-ones limbs (`0xFFFF…`), single set bits, powers of two ± 1, divisors with a top limb of `0x8000…0000` or `0xFFFF…FFFF`, quotient digits that hit `q̂ = 2⁶⁴ − 1`, operands of lengths 1, 2, threshold − 1, threshold and threshold + 1.
- **Deterministic seeds.** A fixed seed in CI, plus a random seed that is printed on failure so it can be replayed.
- **Algebraic identities** as properties: `(a·b)/b == a`, `a == q·b + r` with `|r| < |b|`, `Parse(ToString(a)) == a`, `Karatsuba(a,b) == Schoolbook(a,b)`.
- **Both kernels.** Each test runs against the native and the managed kernels.
- **Fuzzing.** A nightly CI job runs millions of random cases.

## References

- D. Knuth, *The Art of Computer Programming*, vol. 2, §4.3 "Multiple-Precision Arithmetic"
- R. Brent, P. Zimmermann, *Modern Computer Arithmetic* (free PDF), the most practical reference for every algorithm above
- N. Möller, T. Granlund, "Improved division by invariant integers", IEEE TC 2011
- C. Burnikel, J. Ziegler, "Fast Recursive Division", MPI-I-98-1-022
- M. Bodrato, "Towards Optimal Toom-Cook Multiplication for Univariate and Multivariate Polynomials"
- GMP manual, chapter "Algorithms", and the `mpn/x86_64` and `mpn/arm64` sources
