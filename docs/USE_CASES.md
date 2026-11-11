# Use cases

Where arbitrary-precision integers (and the planned big decimals and floats) are used, what each domain needs from a library, and how FullInt fits.

## Summary

| Domain | Typical sizes | Key operations | What matters | FullInt fit |
|---|---|---|---|---|
| Cryptography (RSA, DH, DSA) | 2048–8192 bits (32–128 limbs) | ModPow, ModInverse, primality, GCD | **Constant time**, Montgomery, speed at 32–128 limbs | ⚠️ Research and education only, see below |
| Elliptic-curve crypto | 256–521 bits (4–9 limbs) | modular mul/add with a fixed prime | Constant time, specialized field code | ❌ Use a dedicated library |
| Number theory, competitive math, CAS | 10³–10⁸ bits | mul, div, GCD, pow, roots, primality | Sub-quadratic algorithms, fast `ToString` | ✅ Main target |
| Computing constants (π, e, √2) | 10⁶–10¹⁰ bits | huge mul (NTT), division, sqrt | FFT/NTT, parallelism, streaming output | ✅ Long-term target |
| Engineering and scientific computing | 128–4096-bit mantissas | float ops, sqrt, exp, log | `BigFloat`, controlled rounding | 🔜 Needs `BigFloat` |
| Finance, accounting, billing | 20–60 decimal digits | exact decimal + − ×, ÷ with rounding | `BigDecimal`, exact rounding modes | 🔜 Needs `BigDecimal` |
| Blockchain and tokens | 256 bits (`uint256`) | add, sub, mul, div, comparisons | Fixed width, overflow semantics | ➖ `UInt256` structs are faster for fixed width |
| Databases and IDs | 128–512 bits | parse, format, compare | Fast parse and format, compact serialization | ✅ |
| Exact geometry, rational arithmetic | variable | mul, add, GCD | `BigRational`, robust predicates | 🔜 Needs `BigRational` |
| Education | any | everything | Readable algorithms, docs | ✅ |

## Cryptography: be careful

Today FullInt is **not suitable for production cryptography**, for structural reasons:

1. **Timing side channels.** Execution time depends on the data: early exits in compare, skipping zero limbs, variable-length normalization, data-dependent quotient correction loops. RSA and DH private-key operations then leak key bits through timing.
2. **No secret-memory hygiene.** Intermediate buffers are not zeroed, and the GC may copy them.
3. **No audit.**

Use it to experiment, teach and prototype. For production, use the .NET platform crypto (`System.Security.Cryptography`), which calls OS libraries. If constant-time support is ever added, it should be a **separate API** (`FullIntCt.ModPow`, fixed-width Montgomery ladder, no branches on secrets), not a flag on the general-purpose type.

## Number theory and big computations

This is where 64-bit limbs and asm kernels pay off most, and where `System.Numerics.BigInteger` is weakest (32-bit words, O(n²) `ToString`, no NTT). Examples:

- Factorials, binomials, Fibonacci or Lucas numbers with millions of digits
- Mersenne and other special-form primality testing (Lucas–Lehmer needs fast squaring mod 2ᵖ − 1)
- Continued fractions, Pell equations, partition numbers
- Verifying results from Project Euler style problems

Requirements: Karatsuba → Toom → NTT ladder, dedicated squaring, D&C radix conversion, streaming `ToString` for huge outputs.

## Engineering and scientific computing (with `BigFloat`)

Most engineering code uses `double`. High precision matters in a few cases:

- **Ill-conditioned linear systems** and polynomial root finding, where double precision loses all significant digits
- **Validating numerical algorithms**: computing a reference result with 200+ bits to measure the error of a `double` implementation
- **Long-running simulations** (orbital mechanics, chaotic systems) where rounding error accumulates
- **Summation of many terms** with cancellation

Requirements: binary floating point with a user-chosen precision (MPFR-like), correct rounding modes, `sqrt`, `exp`, `log`, `sin`/`cos`, π.

## Finance (with `BigDecimal`)

`System.Decimal` has 28–29 significant digits and no control over overflow. A `BigDecimal` covers:

- Very large aggregates (national-scale ledgers, crypto-asset amounts with 18 decimal places)
- Interest and amortization schedules that must match regulators' rounding to the last digit
- Currency conversion chains without intermediate rounding

Requirements: exact `+ − ×`, division with an explicit scale and rounding mode (HalfEven, HalfUp, Down, …), round-trip formatting.

## Streaming and parallel scenarios

| Scenario | What helps |
|---|---|
| Printing or saving a 100-million-digit result | `WriteTo(Stream / TextWriter / IBufferWriter<byte>)` with D&C conversion that emits chunks as they are ready |
| Reading a huge number from disk or the network | `ParseAsync(Stream)` / `Parse(ReadOnlySequence<byte>)` |
| Spigot-style digit generation (π digit by digit) | `IAsyncEnumerable<ReadOnlyMemory<byte>>` on top of the core |
| Huge multiplications on multi-core machines | Parallel Karatsuba/Toom top levels and a parallel NTT |

Arithmetic itself is CPU-bound. `async` is only useful at I/O boundaries, and parallelism only pays off above about 10⁵ bits.

## When to use something else

| If you need… | Use |
|---|---|
| Built-in, stable, small-to-medium integers | `System.Numerics.BigInteger` |
| Fixed 128-bit integers | `Int128` / `UInt128` (.NET 7+) |
| Fixed 256-bit integers (EVM) | a dedicated `UInt256` struct library |
| Production cryptography | `System.Security.Cryptography`, BouncyCastle |
| Maximum performance regardless of platform | GMP / MPFR through a native wrapper |
