# Karatsuba — 2026-11-14

Karatsuba multiplication in C# (`FullInt.Core/Karatsuba.cs`) over the author's column asm `Mult`,
add and sub. Subtractive form, scratch from `ArrayPool`, unbalanced operands in chunks.
Threshold: **80 limbs** in the shorter operand.

| | |
|---|---|
| CPU | AMD Ryzen 7 7730U (Zen 3), laptop on AC power |
| OS / runtime | Windows 11 26200, .NET 8.0.27 x64 RyuJIT AVX2 |
| Raw data | [2026-11-14-karatsuba-api-mult.csv](2026-11-14-karatsuba-api-mult.csv) |

## Threshold tuning (`FullInt.Benchmarks kara 15`)

N×N products on the native core, median of 15 alternating rounds. Ratio = Karatsuba / basecase.

| N | basecase | T=32 | T=48 | T=64 | **T=80** | T=96 | T=128 | T=160 |
|---|---|---|---|---|---|---|---|---|
| 48 | 2.1 µs | 1.37 | 1.20 | 1.03 | 1.01 | 1.00 | 1.00 | 1.01 |
| 64 | 3.2 µs | 1.40 | 1.14 | 1.00 | 1.02 | 0.98 | 0.98 | 0.98 |
| 96 | 6.5 µs | 1.07 | 1.07 | 0.97 | **0.93** | 0.93 | 1.01 | 1.00 |
| 128 | 11.0 µs | 1.38 | 0.95 | 0.99 | **0.90** | 0.87 | 0.88 | 1.01 |
| 160 | 16.9 µs | 1.10 | 0.86 | 0.85 | **0.86** | 0.87 | 0.85 | 0.86 |
| 192 | 27.5 µs | 1.01 | 0.86 | 0.75 | **0.81** | 0.75 | 0.92 | 0.88 |
| 256 | 45.9 µs | 1.07 | 0.74 | 0.78 | **0.70** | 0.74 | 0.71 | 0.82 |
| 384 | 86.3 µs | 0.57 | 0.58 | 0.58 | **0.57** | 0.58 | 0.64 | 0.64 |
| 512 | 165 µs | 0.56 | 0.55 | 0.50 | **0.54** | 0.54 | 0.53 | 0.63 |
| 1024 | 727 µs | 0.48 | 0.41 | 0.51 | **0.52** | 0.47 | 0.46 | 0.57 |
| 2048 | 2.89 ms | 0.34 | 0.30 | 0.29 | **0.31** | 0.33 | 0.32 | 0.39 |
| 4096 | 9.92 ms | 0.26 | 0.24 | 0.24 | **0.25** | 0.24 | 0.24 | 0.30 |

The optimum is a flat band at 64–96 (single cells move by ±5–10% between runs); 80 is its middle.
Karatsuba is not called below the threshold, so smaller sizes are unchanged.

**Why the threshold is high** (GMP uses ~30 limbs on x86-64): one C# → asm call costs about
40–50 ns even for a one-word add (measured: `Add` 1 word 53 ns, `Sub` 8 words 40 ns, `Mult` 8×8
77 ns, `ArrayPool` rent+return ~15–50 ns). One Karatsuba level makes five add/sub calls besides the three
products, so on small blocks the overhead eats the saved quarter of the work. Ways down: a cheaper
transition (`SuppressGCTransition` / function pointers, not in `netstandard2.1`) or one fused asm
kernel per level.

## FullInt64 `*` vs BigInteger (ApiBenchmarks, ShortRun)

| N limbs | FullInt64 | BigInteger | ratio | before Karatsuba (same day, 3 runs) |
|---|---|---|---|---|
| 1 | 18.3 ns | 32.9 ns | 0.56 | 0.59 (one-word fast path) |
| 4 | 52.0 ns | 101 ns | 0.51 | 0.49–0.56 |
| 16 | 306 ns | 1.07 µs | 0.29 | 0.24–0.30 |
| 64 | 3.26 µs | 11.0 µs | 0.30 | 0.25–0.30 |
| 256 | 33.7 µs | 136 µs | **0.25** | 0.39–0.51 |
| 1024 | 362 µs | 1.17 ms | **0.31** | 0.66–0.94 |
| 4096 | 3.02 ms | 7.59 ms | **0.40** | — |

Allocated bytes match BigInteger at every size (the Karatsuba scratch comes from the pool).

BigInteger's own Karatsuba no longer closes the gap at 1024 limbs. At 4096 the ratio grows
again (0.31 → 0.40): both are O(n^1.585) there, and ours pays the call overhead per level.
Toom-3 is the next step for that range.

## Raw report

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.300
  [Host]   : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  Categories=Mult  

```
| Method   | N    | Mean            | Error           | StdDev         | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |----- |----------------:|----------------:|---------------:|------:|--------:|-------:|----------:|------------:|
| **Mult**     | **1**    |        **18.34 ns** |        **15.52 ns** |       **0.851 ns** |  **0.56** |    **0.03** | **0.0086** |      **72 B** |        **1.80** |
| Mult_Big | 1    |        32.90 ns |        26.52 ns |       1.454 ns |  1.00 |    0.05 | 0.0048 |      40 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **4**    |        **52.02 ns** |        **61.94 ns** |       **3.395 ns** |  **0.51** |    **0.03** | **0.0143** |     **120 B** |        **1.36** |
| Mult_Big | 4    |       101.45 ns |        63.46 ns |       3.478 ns |  1.00 |    0.04 | 0.0105 |      88 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **16**   |       **306.20 ns** |       **353.23 ns** |      **19.362 ns** |  **0.29** |    **0.03** | **0.0372** |     **312 B** |        **1.11** |
| Mult_Big | 16   |     1,070.18 ns |     2,036.96 ns |     111.653 ns |  1.01 |    0.13 | 0.0324 |     280 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **64**   |     **3,262.82 ns** |     **3,841.95 ns** |     **210.590 ns** |  **0.30** |    **0.02** | **0.1259** |    **1080 B** |        **1.03** |
| Mult_Big | 64   |    11,002.85 ns |    12,339.78 ns |     676.385 ns |  1.00 |    0.08 | 0.1221 |    1048 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **256**  |    **33,743.78 ns** |    **27,835.34 ns** |   **1,525.749 ns** |  **0.25** |    **0.02** | **0.4883** |    **4152 B** |        **1.01** |
| Mult_Big | 256  |   135,590.68 ns |   175,271.09 ns |   9,607.200 ns |  1.00 |    0.09 | 0.4883 |    4121 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **1024** |   **362,460.22 ns** |   **182,613.35 ns** |  **10,009.654 ns** |  **0.31** |    **0.02** | **1.9531** |   **16440 B** |        **1.00** |
| Mult_Big | 1024 | 1,168,680.14 ns | 1,390,066.34 ns |  76,194.227 ns |  1.00 |    0.08 | 1.9531 |   16409 B |        1.00 |
|          |      |                 |                 |                |       |         |        |           |             |
| **Mult**     | **4096** | **3,018,773.11 ns** | **2,977,160.23 ns** | **163,188.199 ns** |  **0.40** |    **0.03** | **3.9063** |   **65595 B** |        **0.99** |
| Mult_Big | 4096 | 7,594,314.06 ns | 7,174,393.80 ns | 393,252.734 ns |  1.00 |    0.06 | 7.8125 |   66587 B |        1.00 |
