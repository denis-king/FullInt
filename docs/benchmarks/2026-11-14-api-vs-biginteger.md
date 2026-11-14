# FullInt64 vs BigInteger — 2026-11-14

Public API level: `FullInt64` operators against `System.Numerics.BigInteger` (.NET 8), same operands.
Run on `3404270`: the original algorithms plus the Mult inner-loop fix (per-column bounds, unroll by 2).
Still schoolbook multiplication on the FullInt side; BigInteger in .NET 8 switches to Karatsuba
above 32 of its 32-bit words.

| | |
|---|---|
| Commit | `3404270` |
| CPU | AMD Ryzen 7 7730U (Zen 3), laptop on AC power |
| OS / runtime | Windows 11 26200, .NET 8.0.27 x64 RyuJIT AVX2 |
| Tool | BenchmarkDotNet 0.14.0, **ShortRun** (3 warmup + 3 iterations), `--filter *ApiBenchmarks.Add* *ApiBenchmarks.Mult* *ApiBenchmarks.Div*` (36 cases, 6 min) |
| What is measured | operator call including result allocation, sign handling and normalization; native asm core |
| Operands | fixed seeds (`Data.Limbs`), N limbs of 64 bits; a positive, b negative; Div = 2N / N limbs |
| Raw data | [2026-11-14-api-vs-biginteger.csv](2026-11-14-api-vs-biginteger.csv) |

ShortRun has wide error bars (see the raw report): read ratios as ±10–15%, not exact.

## Summary: FullInt64 time / BigInteger time (below 1 = FullInt is faster)

| Op | N=1 | 4 | 16 | 64 | 256 | 1024 |
|---|---|---|---|---|---|---|
| **Mult** N×N | 1.01 | **0.56** | **0.25** | **0.27** | **0.42** | **0.66** |
| **Div** 2N/N | 1.57 | 1.16 | **0.63** | **0.30** | **0.10** | **0.10** |
| Add | 1.43 | 1.95 | 1.33 | 0.92 | 0.91 | 0.91 |

Absolute medians:

| Op | N | FullInt64 | BigInteger |
|---|---|---|---|
| Mult | 64 | 4.83 µs | 17.95 µs |
| Mult | 1024 | 0.82 ms | 1.24 ms |
| Div | 256 | 0.15 ms | 1.57 ms |
| Div | 1024 | 2.68 ms | 26.2 ms |
| Add | 1 | 64 ns | 45 ns |
| Add | 1024 | 3.94 µs | 4.33 µs |

## Observations

- **Mult**: 4× faster than BigInteger at 16–64 limbs. The lead shrinks at 256–1024 limbs
  (0.42 → 0.66) because BigInteger uses Karatsuba there while FullInt is still schoolbook.
  Karatsuba over the asm basecase is the next step.
- **Div**: 10× faster from 256 limbs: BigInteger's .NET 8 division is slow at these sizes.
- **Small numbers (1–4 limbs)**: FullInt is slower on Add and Div. The cost is the wrapper, not
  the asm: P/Invoke transition, extra allocation (see below), sign and normalization logic.
- **Allocation**: FullInt allocates about 2× the bytes of BigInteger in Add (all sizes) and in Mult
  at 1024 limbs, and 2–5× in Div. Likely an intermediate buffer plus a trimmed copy of the result;
  worth removing.

## Update: one allocation per result (same day)

`FullInt64Engine`: Add allocates the exact result size and widens by one word only on a carry
out; results no longer go through an always-copying `DeleteZeros(...).ToArray()`. Small buffers
(up to 1024 words) stay on the stack and are copied out once at the trimmed size; larger ones are
heap arrays that become the result unless they have leading zero words. Same ShortRun setup.
Raw data: [2026-11-14-api-vs-biginteger-after-alloc-fix.csv](2026-11-14-api-vs-biginteger-after-alloc-fix.csv).

FullInt64 time / BigInteger time, before → after:

| Op | N=1 | 4 | 16 | 64 | 256 | 1024 |
|---|---|---|---|---|---|---|
| Add | 1.43 → 1.36 | 1.95 → 1.24 | 1.33 → **0.84** | 0.92 → **0.65** | 0.91 → **0.67** | 0.91 → **0.63** |
| Mult | 1.01 → 1.03 | 0.56 → 0.49 | 0.25 → 0.24 | 0.27 → 0.25 | 0.42 → 0.39 | 0.66 → 0.94 * |
| Div | 1.57 → 1.32 | 1.16 → 1.31 | 0.63 → 0.69 | 0.30 → 0.21 | 0.10 → 0.08 | 0.10 → 0.09 |

Allocated bytes, before → after (BigInteger in brackets):

| Op | N=1 | 1024 |
|---|---|---|
| Add | 104 → 64 (32) | 16 472 → **8 248** (8 216) |
| Mult | 72 → 72 (40) | 32 848 → **16 440** (16 409) |
| Div | 200 → 200 (40) | 24 785 → 24 784 (8 239) |

\* Mult at 1024 limbs did not change in the code path being measured (heap buffer in both
versions; only the final copy is gone). Over three ShortRun runs that day the ratio was 0.66,
0.80 and 0.94, mostly because the BigInteger side moved (1.24, 0.85, 0.93 ms): ShortRun on a
laptop is not stable enough at this size. Either way BigInteger's Karatsuba is close here.

What is left:
- **Div** still allocates 3× BigInteger: `/` computes and allocates the remainder too and then
  drops it.
- **1–4 limbs**: the P/Invoke transition and the wrapper dominate (a managed fast path is planned).

## Update 2: "/" and "%" allocate only their result, one-word fast paths (same day)

- `/` no longer materializes the remainder and `%` no longer materializes the quotient;
  the divide-by-zero check no longer allocates a `FullInt64` for the literal `0`.
- One word op one word (`+`, `-`, `*`, `/`, `%`, binary mode): plain 64-bit arithmetic in C#,
  no native call. At this size the P/Invoke transition costs more than the operation.

Same ShortRun setup. Raw data: [2026-11-14-api-vs-biginteger-after-fast-paths.csv](2026-11-14-api-vs-biginteger-after-fast-paths.csv).
In this suite Div at N=1 is 2 words / 1 word, so it does not take the one-word path.

FullInt64 time / BigInteger time, first run → after the allocation fix → now:

| Op | N=1 | 4 | 16 | 64 | 256 | 1024 |
|---|---|---|---|---|---|---|
| Add | 1.43 → 1.36 → **0.71** | 1.95 → 1.24 → **0.91** | 1.33 → 0.84 → 0.84 | 0.92 → 0.65 → 0.58 | 0.91 → 0.67 → 0.60 | 0.91 → 0.63 → 0.60 |
| Mult | 1.01 → 1.03 → **0.59** | 0.56 → 0.49 → 0.52 | 0.25 → 0.24 → 0.30 | 0.27 → 0.25 → 0.30 | 0.42 → 0.39 → 0.51 | 0.66 → 0.94 → 0.80 |
| Div | 1.57 → 1.32 → 1.03 | 1.16 → 1.31 → 1.07 | 0.63 → 0.69 → 0.77 | 0.30 → 0.21 → 0.21 | 0.10 → 0.08 → 0.06 | 0.10 → 0.09 → 0.07 |

Div allocated bytes: N=1 200 → **72** (BigInteger 40), N=4 240 → **88** (56),
N=256 4 272 → **2 104** (2 073), N=1024 24 784 → **16 473** (8 239).

Mult and Add between 4 and 1024 limbs did not change in code; their spread between runs
(±15–25%) is ShortRun noise on a laptop, mostly on the BigInteger side.

What is left:
- **Div at 1024+ limbs** allocates 2× BigInteger: the remainder work buffer (divisor + 2 words)
  no longer fits the 1024-word stack limit and goes to the heap. An `ArrayPool` would remove it.
- **N=1**: our result is a class plus an array (64–72 B), BigInteger is a struct plus an array (32–40 B).

## Raw report (first run, before the allocation fix)

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
Unknown processor
.NET SDK 10.0.300
  [Host]   : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Categories | N    | Mean             | Error           | StdDev         | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------- |----------- |----- |-----------------:|----------------:|---------------:|------:|--------:|-------:|-------:|----------:|------------:|
| **Add**      | **Add**        | **1**    |         **63.77 ns** |        **72.40 ns** |       **3.969 ns** |  **1.43** |    **0.17** | **0.0124** |      **-** |     **104 B** |        **3.25** |
| Add_Big  | Add        | 1    |         44.89 ns |        92.99 ns |       5.097 ns |  1.01 |    0.15 | 0.0038 |      - |      32 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Add**      | **Add**        | **4**    |         **83.36 ns** |        **78.48 ns** |       **4.302 ns** |  **1.95** |    **0.13** | **0.0181** |      **-** |     **152 B** |        **2.71** |
| Add_Big  | Add        | 4    |         42.85 ns |        43.43 ns |       2.380 ns |  1.00 |    0.07 | 0.0067 |      - |      56 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Add**      | **Add**        | **16**   |        **111.86 ns** |       **269.88 ns** |      **14.793 ns** |  **1.33** |    **0.21** | **0.0411** |      **-** |     **344 B** |        **2.26** |
| Add_Big  | Add        | 16   |         85.01 ns |       198.97 ns |      10.906 ns |  1.01 |    0.15 | 0.0181 |      - |     152 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Add**      | **Add**        | **64**   |        **317.05 ns** |       **696.50 ns** |      **38.178 ns** |  **0.92** |    **0.10** | **0.1326** |      **-** |    **1112 B** |        **2.07** |
| Add_Big  | Add        | 64   |        346.22 ns |       332.19 ns |      18.208 ns |  1.00 |    0.06 | 0.0639 |      - |     536 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Add**      | **Add**        | **256**  |      **1,111.55 ns** |       **914.16 ns** |      **50.108 ns** |  **0.91** |    **0.07** | **0.4997** | **0.0038** |    **4184 B** |        **2.02** |
| Add_Big  | Add        | 256  |      1,223.08 ns |     1,636.53 ns |      89.704 ns |  1.00 |    0.09 | 0.2470 | 0.0019 |    2072 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Add**      | **Add**        | **1024** |      **3,940.75 ns** |     **1,619.67 ns** |      **88.779 ns** |  **0.91** |    **0.02** | **1.9684** | **0.1144** |   **16472 B** |        **2.00** |
| Add_Big  | Add        | 1024 |      4,326.91 ns |     1,432.67 ns |      78.530 ns |  1.00 |    0.02 | 0.9804 | 0.0305 |    8216 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **1**    |        **140.80 ns** |       **102.73 ns** |       **5.631 ns** |  **1.57** |    **0.10** | **0.0238** |      **-** |     **200 B** |        **5.00** |
| Div_Big  | Div        | 1    |         90.12 ns |        98.03 ns |       5.373 ns |  1.00 |    0.07 | 0.0048 |      - |      40 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **4**    |        **299.68 ns** |       **361.44 ns** |      **19.812 ns** |  **1.16** |    **0.07** | **0.0286** |      **-** |     **240 B** |        **4.29** |
| Div_Big  | Div        | 4    |        258.96 ns |        80.54 ns |       4.415 ns |  1.00 |    0.02 | 0.0067 |      - |      56 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **16**   |      **1,340.31 ns** |       **134.08 ns** |       **7.349 ns** |  **0.63** |    **0.04** | **0.0515** |      **-** |     **440 B** |        **2.75** |
| Div_Big  | Div        | 16   |      2,138.63 ns |     3,042.26 ns |     166.757 ns |  1.00 |    0.10 | 0.0191 |      - |     160 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **64**   |     **12,110.47 ns** |    **18,646.13 ns** |   **1,022.057 ns** |  **0.30** |    **0.05** | **0.1373** |      **-** |    **1208 B** |        **2.22** |
| Div_Big  | Div        | 64   |     41,847.65 ns |   151,172.79 ns |   8,286.291 ns |  1.03 |    0.25 | 0.0610 |      - |     544 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **256**  |    **152,857.72 ns** |    **66,636.24 ns** |   **3,652.557 ns** |  **0.10** |    **0.01** | **0.4883** |      **-** |    **4272 B** |        **2.05** |
| Div_Big  | Div        | 256  |  1,567,047.95 ns | 1,648,263.92 ns |  90,346.907 ns |  1.00 |    0.07 |      - |      - |    2081 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Div**      | **Div**        | **1024** |  **2,678,975.91 ns** | **2,116,080.81 ns** | **115,989.530 ns** |  **0.10** |    **0.00** | **1.9531** |      **-** |   **24785 B** |        **2.83** |
| Div_Big  | Div        | 1024 | 26,180,787.50 ns | 8,648,444.29 ns | 474,050.416 ns |  1.00 |    0.02 |      - |      - |    8752 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **1**    |         **59.46 ns** |        **27.65 ns** |       **1.515 ns** |  **1.01** |    **0.04** | **0.0086** |      **-** |      **72 B** |        **1.80** |
| Mult_Big | Mult       | 1    |         59.06 ns |        36.17 ns |       1.983 ns |  1.00 |    0.04 | 0.0048 |      - |      40 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **4**    |         **96.71 ns** |       **133.01 ns** |       **7.291 ns** |  **0.56** |    **0.05** | **0.0143** |      **-** |     **120 B** |        **1.36** |
| Mult_Big | Mult       | 4    |        174.14 ns |       206.79 ns |      11.335 ns |  1.00 |    0.08 | 0.0105 |      - |      88 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **16**   |        **420.28 ns** |       **278.98 ns** |      **15.292 ns** |  **0.25** |    **0.01** | **0.0372** |      **-** |     **312 B** |        **1.11** |
| Mult_Big | Mult       | 16   |      1,715.45 ns |     1,469.13 ns |      80.528 ns |  1.00 |    0.06 | 0.0324 |      - |     280 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **64**   |      **4,826.15 ns** |     **1,952.11 ns** |     **107.002 ns** |  **0.27** |    **0.01** | **0.1259** |      **-** |    **1080 B** |        **1.03** |
| Mult_Big | Mult       | 64   |     17,954.10 ns |    17,018.52 ns |     932.843 ns |  1.00 |    0.06 | 0.1221 |      - |    1048 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **256**  |     **69,780.67 ns** |    **10,786.96 ns** |     **591.270 ns** |  **0.42** |    **0.01** | **0.4883** |      **-** |    **4152 B** |        **1.01** |
| Mult_Big | Mult       | 256  |    167,929.08 ns |    36,222.67 ns |   1,985.487 ns |  1.00 |    0.01 | 0.4883 |      - |    4121 B |        1.00 |
|          |            |      |                  |                 |                |       |         |        |        |           |             |
| **Mult**     | **Mult**       | **1024** |    **819,437.57 ns** |   **387,256.49 ns** |  **21,226.835 ns** |  **0.66** |    **0.02** | **3.9063** |      **-** |   **32848 B** |        **2.00** |
| Mult_Big | Mult       | 1024 |  1,243,188.22 ns |   707,704.54 ns |  38,791.674 ns |  1.00 |    0.04 | 1.9531 |      - |   16440 B |        1.00 |
