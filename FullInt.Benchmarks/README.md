# FullInt.Benchmarks

Two tools in one project. Always build and run in **Release**.

| Question | Tool | Time |
|---|---|---|
| Did my asm edit make anything slower? | `ab.ps1` (A/B of two native builds) | ~1–2 min |
| How fast is FullInt vs `BigInteger`, native vs managed, by size? | BenchmarkDotNet suites | minutes to an hour |

## 1. Regression check for the asm: `ab.ps1`

```powershell
.\FullInt.Benchmarks\ab.ps1                        # working tree vs HEAD
.\FullInt.Benchmarks\ab.ps1 -Base HEAD~1           # last commit vs the one before
.\FullInt.Benchmarks\ab.ps1 -Filter Div -Rounds 31 # one op, more rounds to confirm a result
```

What it does:

1. Takes the asm sources of `-Base` (any git ref) with `git archive`, builds them in
   `%TEMP%\fullint-ab-<sha>` with the current `build-native.cmd` and the bundled NASM.
   The build is cached per commit.
2. Builds the working tree (`FullInt.Asm\bin\win-x64\Release`).
3. Loads **both DLLs into one process** and calls every export through the same
   function-pointer loop on the same operands. Rounds alternate base/cur order; the
   median and the best-of-rounds time are reported. The process runs at high priority
   on a single core.

Output columns: `base ns`, `cur ns` (median ns per call), `cur/base` (median ratio),
`min c/b` (best-of-rounds ratio). `!` = slower, `+` = faster: only when **both** ratios are
beyond 5% **and** the difference is more than 1 ns.

Noise floor (A/A run, same sources on both sides, Ryzen 7 7730U laptop): ratios stay within about
±5%, single-limb calls within ~1 ns. A one-off `!` is not proof — rerun that op with
`-Filter <op> -Rounds 31`. Close heavy apps and plug in the laptop (power plans change clocks).

Code placement matters on Zen 3: moving a loop across a 32/64-byte boundary has changed
`Add` by 10–30% with no instruction changed. So a change in an unrelated function can move
another one — check the whole table, not just the op you edited.

Multiplication threshold tuning: basecase vs Karatsuba with several thresholds (N = 24…4096), and
Karatsuba alone vs Karatsuba + Toom-3 with several Toom-3 thresholds (N = 128…8192):

```
dotnet run -c Release --project FullInt.Benchmarks -- kara [rounds]
dotnet run -c Release --project FullInt.Benchmarks -- kara toom [rounds]
```

Direct use, with any two DLLs:

```
dotnet run -c Release --project FullInt.Benchmarks -- ab <base.dll> <cur.dll> [opFilter|*] [rounds]
```

## 2. BenchmarkDotNet suites

```
dotnet run -c Release --project FullInt.Benchmarks -- --list flat
dotnet run -c Release --project FullInt.Benchmarks -- --filter *CoreBenchmarks*
dotnet run -c Release --project FullInt.Benchmarks -- --filter *CoreBenchmarks.Mult* --job short
dotnet run -c Release --project FullInt.Benchmarks -- --filter *ApiBenchmarks*
```

Results (markdown/html/csv) go to `BenchmarkDotNet.Artifacts/` (git-ignored).

| Suite | Level | Compares |
|---|---|---|
| `CoreBenchmarks` | `IFullIntCore` on preallocated buffers: no allocation, no sign handling | native asm core vs managed fallback, N = 1…1024 limbs |
| `ApiBenchmarks` | `FullInt64` operators (allocation, signs, normalization included) | FullInt64 vs `System.Numerics.BigInteger` (baseline) |

`ApiBenchmarks` sizes: N = 1…4096 limbs.

Ops: Add, Sub, Mult (N×N), Compare (differs in the last limb), division by one word,
Div (2N / N limbs), And (one negative operand), Shl, decimal ToString / Parse.

`ApiBenchmarks` uses whichever core the library picks (native on win-x64). For the managed
fallback: `$env:FULLINT_FORCE_MANAGED = "1"` before running.

A full `CoreBenchmarks` run is 120 cases, about 3 hours on a laptop; use `--filter` and `--job short` while iterating.

Operands are generated from fixed seeds (`Data.Limbs`), so numbers are comparable between runs.

## Saved baselines

Full runs worth keeping go to [docs/benchmarks/](../docs/benchmarks/) (summary + raw report + csv):

- [2026-11-13-core-baseline.md](../docs/benchmarks/2026-11-13-core-baseline.md) — the original algorithms and asm before any optimization
- [2026-11-14-api-vs-biginteger.md](../docs/benchmarks/2026-11-14-api-vs-biginteger.md) — FullInt64 vs `BigInteger` (Add, Mult, Div), after the Mult inner-loop fix
- [2026-11-14-karatsuba.md](../docs/benchmarks/2026-11-14-karatsuba.md) — Karatsuba threshold tuning and Mult vs `BigInteger` up to 4096 limbs
- [2026-11-14-api-after-optimizations.md](../docs/benchmarks/2026-11-14-api-after-optimizations.md) — FullInt64 vs `BigInteger` (Add, Mult, Div, up to 4096 limbs) after all optimizations of the day
