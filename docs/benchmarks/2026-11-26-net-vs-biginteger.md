# FullInt .NET against BigInteger, 2026-11-26

`FullInt.Benchmarks cross --max 16384`: the same numbers on both sides; each cell is the minimum over rounds of batches of at least 3 ms. Before anything is measured, every operation at every size is warmed on both sides, so the JIT has compiled them all. The process runs pinned to one core at high priority. The numbers below say how many times faster FullInt is (`BigInteger` time / `FullInt` time; above 1 means FullInt is faster). Sizes are in 64-bit words; `c / b` divides 2n words by n words. The raw times in nanoseconds are in the CSV files next to this one, together with subtraction, shifts, AND, compare and the in-place forms.

## Windows x64, .NET 10.0.8 (the current LTS)

AMD Ryzen 7 7730U (Zen 3), Windows 11. The machine was not idle (about 14% background load), so expect some noise in the small cells.

| Operation | 1 | 4 | 16 | 64 | 256 | 1024 | 4096 | 16384 |
|---|---|---|---|---|---|---|---|---|
| `a * b` | 1.8 | 2.7 | 4.4 | 5.0 | 4.8 | 8.5 | 18 | 35 |
| `c / b` | 1.9 | 1.6 | 4.6 | 6.8 | 5.2 | 6.9 | 11 | 23 |
| `ToString()` | 2.4 | 1.1 | 1.5 | 2.9 | 7.1 | 14 | 28 | 97 |
| `Parse` | 1.7 | 2.1 | 2.6 | 2.8 | 3.9 | 4.7 | 6.9 | 10 |
| `a + b` | 1.5 | 1.4 | 2.3 | 2.4 | 3.7 | 3.4 | 2.7 | 2.6 |

FullInt is at least as fast as `BigInteger` in every one of the 228 cells, from a single word up.

## Windows x64, .NET 8.0.27

The same machine and build; only `BigInteger` differs. Its division on .NET 8 is quadratic, which is where the large ratios come from. Between .NET 8 and .NET 10, `BigInteger` got a much faster division (16 ms instead of 457 ms at 4096 words here) and faster text conversion, so the .NET 10 table is the fair one.

| Operation | 1 | 4 | 16 | 64 | 256 | 1024 | 4096 | 16384 |
|---|---|---|---|---|---|---|---|---|
| `a * b` | 1.3 | 2.1 | 4.8 | 5.2 | 5.6 | 9.7 | 22 | 46 |
| `c / b` | 1.3 | 1.4 | 3.4 | 14 | 31 | 80 | 219 | 1128 |
| `ToString()` | 2.6 | 1.6 | 1.9 | 3.8 | 6.8 | 18 | 43 | 109 |
| `Parse` | 2.2 | 2.8 | 2.6 | 2.8 | 3.0 | 7.3 | 15 | 26 |
| `a + b` | 1.5 | 1.1 | 1.4 | 2.4 | 2.8 | 2.7 | 2.8 | 3.1 |

## Linux ARM64, .NET 8.0.34

Oracle Ampere A1 (Neoverse-N1), one core, Docker. This machine has no .NET 10 yet, so the comparison is against .NET 8 and overstates the division ratios in the same way. At 2–4 words, addition, subtraction and `ToString` are on par (0.92–0.99); everything else is faster.

| Operation | 1 | 4 | 16 | 64 | 256 | 1024 | 4096 | 16384 |
|---|---|---|---|---|---|---|---|---|
| `a * b` | 1.4 | 1.7 | 2.3 | 2.6 | 3.1 | 5.4 | 11 | 23 |
| `c / b` | 1.2 | 1.2 | 3.2 | 12 | 15 | 30 | 90 | 305 |
| `ToString()` | 1.8 | 1.0 | 1.1 | 2.1 | 4.6 | 8.3 | 19 | 52 |
| `Parse` | 2.2 | 2.9 | 3.2 | 3.2 | 4.2 | 7.5 | 7.4 | 14 |
| `a + b` | 1.5 | 1.0 | 1.7 | 3.0 | 3.3 | 3.5 | 4.3 | 2.6 |

## Files

- `2026-11-26-net-vs-biginteger-win-x64-net10.csv`
- `2026-11-26-net-vs-biginteger-win-x64-net8.csv`
- `2026-11-26-net-vs-biginteger-arm64-net8.csv`

Reproduce: `dotnet run -c Release --project FullInt.Benchmarks -p:BenchTfm=net10.0 -- cross --max 16384 --csv <file>`.
