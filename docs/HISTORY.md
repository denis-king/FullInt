# How FullInt came to be

## Before the repository: numbers that do not fit

It started as plain curiosity: how do you compute with numbers that do not fit into any
built-in type? Years ago, as a personal side project in C#, I wrote it the way it is done on
paper: digits in a byte array, schoolbook "column" addition, multiplication and division. It
worked and it was slow - and I did not know yet that .NET already had
`System.Numerics.BigInteger`.

I wanted 64-bit words from the start, but there was a catch: in C# a 64 x 64-bit product
gives only its low half, and without the high half there is no long multiplication - so the
digits stayed bytes. Assembly would have had the instruction for it, and I knew assembly: as
a student I wrote 16-bit code in Turbo Assembler. But a 16-bit assembler was all I had, and
with it only byte arithmetic made sense, so the idea stayed an idea.

## October 2025: back to the old solution

Asked about pet projects, I remembered those calculations, took the old solution from an old
computer and put it into git (the first commit, 2025-10-09: `byte[]` digits in base 100 or
256, a `Stack<byte>` for building results, even a SQL CLR type). Then:

- found `BigInteger` and started comparing against it - speed and correctness tests;
- fixed the division bugs, rewrote division along the lines of Knuth's algorithm D;
- replaced the temporary lists and stacks with arrays, `Span` and `stackalloc`;
- moved from byte digits to 64-bit words (`FullInt64`, 2025-11-01).

## November - December 2025: assembly

With 64-bit words the obvious next step was the CPU's own 64 x 64 -> 128-bit multiply and
128 / 64-bit divide, one instruction each. An AI assistant pointed out that a 64-bit assembler
is easy to get and to link into .NET: first MASM (Windows only), then NASM (cross-platform).

The first attempt - calling single native instructions from C# - gained nothing: the call cost
more than the instruction. The second one did: moving whole operations with their hot loops
into assembly (2025-12-13). Addition and division came close to `BigInteger`, multiplication
overtook it. It looked like a win - until I learned about GMP, which has used hand-written
assembly for decades. From then on the goal was clear: a library that is not close to GMP
has little reason to exist.

Decimal conversion and parsing moved to assembly in January 2026. Then a long break.

## November 2026: the race against GMP

Coming back to the project, now with an AI pair programmer, in a few weeks:

- **multiplication:** Karatsuba, Toom-3 / 4 / 6, Schönhage-Strassen FFT, AVX2
  number-theoretic transforms; short products, squares at every level;
- **division:** precomputed reciprocals (Möller-Granlund), Burnikel-Ziegler, Newton
  division, an approximate quotient when the remainder is not needed;
- **kernels per CPU:** a baseline set plus `mulx` / `adcx` / `adox` (BMI2 + ADX) and AVX2
  versions chosen at start-up by `cpuid`; register-resident kernels for short numbers;
- **platforms:** a 32-bit x86 port of every kernel, a managed fallback core, .NET Framework
  through netstandard2.0;
- **C++:** a native library on the same kernels (FullInt.Cpp), the only way to compare with
  GMP without the .NET call overheads.

## Where it stands (November 2026, x64, against GMP 6.3)

- multiplication: 0.75-1.1 of GMP's time on small numbers, about 0.5 on 4096 words;
- division: 0.55-1.0 on most sizes;
- decimal text and parsing: 0.75-1.1;
- shifts, bitwise operations, comparison: about half of GMP's time from a few hundred words;
- against `BigInteger` (FullInt .NET): from 8 words on 4-20 times faster multiplication,
  3-200+ times faster division, 3-38 times faster `ToString`.

Still open: the short-number API overheads (operators returning a new value), 32-bit x86
division on mid-size numbers, Linux and ARM64 builds.
