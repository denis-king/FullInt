# Changelog

One version for the NuGet package and the C++ library. The publish workflow requires a section
`## <version>` here and takes it for the package's release notes on nuget.org and for the GitHub
Release. Within a section: **Breaking changes** first (before 1.0.0 allowed in any preview; after
it only in a new major version, an API first marked `[Obsolete]` in a minor one), then Added,
Changed, Fixed, and a line for each of .NET and C++ ("no changes" when it has none).

## 1.0.0-preview.1

The first public version: the .NET package `FullInt` on nuget.org and the C++ library from this
repository (tag `v1.0.0-preview.1`).

- **.NET:** `FullNumerics.FullInt`, an alternative to `System.Numerics.BigInteger` with the same
  API (operators, `Parse` / `ToString` with the D / X / B formats, bytes, 64-bit words, conversions,
  System.Text.Json, generic math on .NET 7+) and in-place arithmetic (`FullInt.Add(result, a, b)`
  and its siblings). Builds: `net8.0` and `netstandard2.0` (.NET Framework 4.6.1+, Mono, Unity).
- **Native kernels in the package:** win-x64, win-x86, linux-x64, linux-arm64, linux-arm (ARMv7);
  every other platform (macOS, Windows ARM64, iOS, Android, WebAssembly) runs the managed core -
  the same results, slower. CI installs and checks the package on Windows x64 / x86 / ARM64, Linux
  x64 / ARM64 / ARMv7, macOS (Apple Silicon, Intel) and in an iOS app on the iOS simulator.
- **C++:** `fullnumerics::FullInt` on the same kernels (Windows x64 / x86, Linux x64, ARM64, ARMv7;
  macOS on Apple Silicon builds and passes its tests); `find_package`, `add_subdirectory`,
  `FetchContent`.
- **Algorithms:** Karatsuba, Toom-3 / 4 / 6, NTT (AVX2 on x86, NEON on ARM), Schönhage–Strassen;
  Burnikel–Ziegler and Newton division; divide-and-conquer decimal conversion.
- **Speed** (docs/benchmarks/2026-11-27-all-platforms.md): .NET 10 on x64 1.1-97x `BigInteger`;
  C++ at or ahead of GMP 6.3 on x64 and x86 in almost every cell.
- **Known gaps:** on ARM64 the C++ division and `ToString` take 1.1-1.5x GMP's time; on 32-bit ARM
  .NET operations on 1-8 words are slower than `BigInteger` (the runtime's 8-byte-aligned array
  allocation there).
