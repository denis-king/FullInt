# Core baseline — 2026-11-13

**The author's original algorithms and hand-written asm**, with only the pointwise fixes from
`c65e1a0`: Win64 ABI wrappers, division correctness fixes, new bitwise/shift kernels. Every
later optimization is measured against this file.

| | |
|---|---|
| Asm sources | `c65e1a0` (unchanged up to `d75c1ff`, the commit this was run on) |
| CPU | AMD Ryzen 7 7730U (Zen 3), 8 cores / 16 threads, laptop on AC power |
| OS / runtime | Windows 11 26200, .NET 8.0.27 x64 RyuJIT AVX2 |
| Tool | BenchmarkDotNet 0.14.0, DefaultJob, `--filter *CoreBenchmarks*` (120 cases, 3 h 11 min) |
| What is measured | `IFullIntCore` on preallocated buffers: no allocation, no sign handling; P/Invoke transition included for native |
| Operands | fixed seeds (`Data.Limbs`), N limbs of 64 bits; Div = 2N / N limbs; Compare differs in the last limb |
| Raw data | [2026-11-13-core-baseline.csv](2026-11-13-core-baseline.csv) |

## Summary (median)

| Op | N=1 | 4 | 16 | 64 | 256 | 1024 | native vs managed @1024 |
|---|---|---|---|---|---|---|---|
| **Mult** N×N | 18.5 ns | 34.5 ns | 419 ns | 7.22 µs | 101 µs | 1.50 ms | 8.4× |
| **Div** 2N/N | 27.8 ns | 130 ns | 879 ns | 8.91 µs | 104 µs | 1.78 ms | 7.8× |
| Add | 15.7 ns | 23.4 ns | 36.4 ns | 91.9 ns | 323 ns | 1.27 µs | 3.2× |
| Sub | 12.2 ns | 18.3 ns | 40.8 ns | 104 ns | 434 ns | 1.46 µs | 2.3× |
| DivByWord | 15.3 ns | 23.9 ns | 79.1 ns | 328 ns | 1.29 µs | 5.14 µs | 2.6× |
| ToDecString | 85.4 ns | 296 ns | 1.59 µs | 14.0 µs | 188 µs | 2.82 ms | 3.4× |
| FromDecString | 54.6 ns | 150 ns | 694 ns | 5.50 µs | 64.2 µs | 0.94 ms | 5.1× |
| And (one negative) | 24.0 ns | 29.4 ns | 47.7 ns | 126 ns | 422 ns | 1.61 µs | 2.1× |
| Compare | 11.7 ns | 13.4 ns | 21.3 ns | 61.3 ns | 134 ns | 655 ns | 1.5× |
| ShiftLeft | 15.7 ns | 18.5 ns | 31.6 ns | 84.2 ns | 300 ns | 1.25 µs | 1.1× |

## Observations (where to improve next)

- **Mult / Div**: the asm is 4–8× faster than managed from 64 limbs up. Div costs about the same as
  Mult of the same size, and both grow as N², as expected for schoolbook.
- **Mult inner loop**: about 1.4–1.8 ns per limb product (1024² ≈ 1M products in 1.5 ms). A tuned
  `mulx` + `adcx`/`adox` basecase on Zen 3 is roughly 1.5–2 cycles per product, so the basecase alone
  has room for about 3–4×. Karatsuba and Toom-3 come on top of that.
- **Add / Sub**: about 1.2 ns per limb. The loop checks per word whether A and B still have words,
  and moves the carry through `setc`/`shr`. Splitting the loop into a common part and a tail
  should give about 2–3×.
- **N = 1**: native is often slower than managed (Add, Compare, And, Shl, DivByWord). The 5–8 ns go to
  the P/Invoke transition (pinning, `out`/`ref` marshaling), not to the asm. This is a C# fix
  (`SuppressGCTransition`, function pointers, a managed path for tiny operands).
- **ShiftLeft**: the JIT emits the same shift code, so the asm gives little here.

## Raw BenchmarkDotNet report

```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
AMD Ryzen 7 7730U with Radeon Graphics (Zen 3)
.NET SDK 10.0.300
  [Host]     : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.27 (8.0.2726.22922), X64 RyuJIT AVX2


```
| Method        | N    | Core    | Mean              | Error           | StdDev            | Median            | Allocated |
|-------------- |----- |-------- |------------------:|----------------:|------------------:|------------------:|----------:|
| **Add**           | **1**    | **managed** |         **15.342 ns** |       **0.7997 ns** |         **2.2161 ns** |         **15.102 ns** |         **-** |
| **Add**           | **1**    | **native**  |         **15.738 ns** |       **0.5076 ns** |         **1.3897 ns** |         **15.678 ns** |         **-** |
| **Add**           | **4**    | **managed** |         **31.172 ns** |       **0.9013 ns** |         **2.5275 ns** |         **30.804 ns** |         **-** |
| **Add**           | **4**    | **native**  |         **23.392 ns** |       **0.8240 ns** |         **2.3509 ns** |         **23.366 ns** |         **-** |
| **Add**           | **16**   | **managed** |         **80.974 ns** |       **1.8952 ns** |         **5.3763 ns** |         **79.725 ns** |         **-** |
| **Add**           | **16**   | **native**  |         **36.469 ns** |       **1.2302 ns** |         **3.5297 ns** |         **36.351 ns** |         **-** |
| **Add**           | **64**   | **managed** |        **268.272 ns** |       **5.4000 ns** |        **11.6240 ns** |        **266.664 ns** |         **-** |
| **Add**           | **64**   | **native**  |         **93.105 ns** |       **2.4679 ns** |         **7.1598 ns** |         **91.919 ns** |         **-** |
| **Add**           | **256**  | **managed** |      **1,062.265 ns** |      **22.1280 ns** |        **62.7736 ns** |      **1,051.036 ns** |         **-** |
| **Add**           | **256**  | **native**  |        **327.883 ns** |       **8.5870 ns** |        **24.7753 ns** |        **323.011 ns** |         **-** |
| **Add**           | **1024** | **managed** |      **4,058.861 ns** |      **81.1865 ns** |       **178.2063 ns** |      **4,010.422 ns** |         **-** |
| **Add**           | **1024** | **native**  |      **1,287.112 ns** |      **38.1524 ns** |       **111.2925 ns** |      **1,265.454 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **AndNegative**   | **1**    | **managed** |         **18.691 ns** |       **0.6168 ns** |         **1.8186 ns** |         **18.483 ns** |         **-** |
| **AndNegative**   | **1**    | **native**  |         **24.169 ns** |       **0.7406 ns** |         **2.1486 ns** |         **24.047 ns** |         **-** |
| **AndNegative**   | **4**    | **managed** |         **29.665 ns** |       **1.0568 ns** |         **3.0995 ns** |         **29.355 ns** |         **-** |
| **AndNegative**   | **4**    | **native**  |         **29.567 ns** |       **0.9004 ns** |         **2.6406 ns** |         **29.380 ns** |         **-** |
| **AndNegative**   | **16**   | **managed** |         **69.408 ns** |       **1.9852 ns** |         **5.8534 ns** |         **69.383 ns** |         **-** |
| **AndNegative**   | **16**   | **native**  |         **47.425 ns** |       **1.3004 ns** |         **3.7933 ns** |         **47.670 ns** |         **-** |
| **AndNegative**   | **64**   | **managed** |        **243.246 ns** |       **7.0696 ns** |        **20.8450 ns** |        **239.948 ns** |         **-** |
| **AndNegative**   | **64**   | **native**  |        **126.480 ns** |       **2.9182 ns** |         **8.5126 ns** |        **125.690 ns** |         **-** |
| **AndNegative**   | **256**  | **managed** |        **750.914 ns** |      **21.4880 ns** |        **63.0205 ns** |        **753.331 ns** |         **-** |
| **AndNegative**   | **256**  | **native**  |        **422.808 ns** |      **10.6830 ns** |        **31.4990 ns** |        **421.748 ns** |         **-** |
| **AndNegative**   | **1024** | **managed** |      **3,455.713 ns** |     **116.2088 ns** |       **342.6444 ns** |      **3,369.361 ns** |         **-** |
| **AndNegative**   | **1024** | **native**  |      **1,608.600 ns** |      **38.7625 ns** |       **114.2921 ns** |      **1,605.021 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **Compare**       | **1**    | **managed** |          **8.963 ns** |       **0.2767 ns** |         **0.8114 ns** |          **9.045 ns** |         **-** |
| **Compare**       | **1**    | **native**  |         **11.783 ns** |       **0.3626 ns** |         **1.0634 ns** |         **11.694 ns** |         **-** |
| **Compare**       | **4**    | **managed** |         **13.012 ns** |       **0.4041 ns** |         **1.1851 ns** |         **13.043 ns** |         **-** |
| **Compare**       | **4**    | **native**  |         **13.455 ns** |       **0.3900 ns** |         **1.1378 ns** |         **13.374 ns** |         **-** |
| **Compare**       | **16**   | **managed** |         **25.725 ns** |       **0.7617 ns** |         **2.2458 ns** |         **25.604 ns** |         **-** |
| **Compare**       | **16**   | **native**  |         **21.585 ns** |       **0.6586 ns** |         **1.9419 ns** |         **21.349 ns** |         **-** |
| **Compare**       | **64**   | **managed** |         **99.982 ns** |       **3.7456 ns** |        **11.0440 ns** |         **99.549 ns** |         **-** |
| **Compare**       | **64**   | **native**  |         **61.672 ns** |       **1.2547 ns** |         **3.5796 ns** |         **61.285 ns** |         **-** |
| **Compare**       | **256**  | **managed** |        **223.548 ns** |      **12.6055 ns** |        **37.1675 ns** |        **206.741 ns** |         **-** |
| **Compare**       | **256**  | **native**  |        **147.167 ns** |      **10.4041 ns** |        **30.6767 ns** |        **133.506 ns** |         **-** |
| **Compare**       | **1024** | **managed** |        **941.210 ns** |      **50.6092 ns** |       **149.2225 ns** |        **995.938 ns** |         **-** |
| **Compare**       | **1024** | **native**  |        **666.158 ns** |      **20.2803 ns** |        **59.7970 ns** |        **654.670 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **Div**           | **1**    | **managed** |         **67.511 ns** |       **1.9093 ns** |         **5.5997 ns** |         **67.483 ns** |         **-** |
| **Div**           | **1**    | **native**  |         **27.823 ns** |       **0.8091 ns** |         **2.3856 ns** |         **27.797 ns** |         **-** |
| **Div**           | **4**    | **managed** |        **273.315 ns** |       **6.0457 ns** |        **17.8260 ns** |        **272.027 ns** |         **-** |
| **Div**           | **4**    | **native**  |        **131.272 ns** |       **2.6617 ns** |         **6.3259 ns** |        **130.124 ns** |         **-** |
| **Div**           | **16**   | **managed** |      **2,585.568 ns** |      **78.9958 ns** |       **231.6811 ns** |      **2,592.604 ns** |         **-** |
| **Div**           | **16**   | **native**  |        **884.315 ns** |      **17.6633 ns** |        **47.1469 ns** |        **879.066 ns** |         **-** |
| **Div**           | **64**   | **managed** |     **28,908.758 ns** |     **804.8397 ns** |     **2,373.0881 ns** |     **28,590.056 ns** |         **-** |
| **Div**           | **64**   | **native**  |      **8,870.689 ns** |     **176.9301 ns** |       **518.9053 ns** |      **8,908.789 ns** |         **-** |
| **Div**           | **256**  | **managed** |    **769,601.445 ns** |  **15,182.9432 ns** |    **36,957.3848 ns** |    **771,033.252 ns** |         **-** |
| **Div**           | **256**  | **native**  |    **104,080.137 ns** |   **3,058.9946 ns** |     **9,019.5153 ns** |    **104,346.729 ns** |         **-** |
| **Div**           | **1024** | **managed** | **13,787,279.932 ns** | **274,064.2010 ns** |   **426,684.9775 ns** | **13,868,667.188 ns** |       **6 B** |
| **Div**           | **1024** | **native**  |  **1,788,253.042 ns** |  **45,748.0909 ns** |   **134,171.2410 ns** |  **1,778,267.383 ns** |       **1 B** |
|               |      |         |                   |                 |                   |                   |           |
| **DivByWord**     | **1**    | **managed** |         **13.486 ns** |       **0.4630 ns** |         **1.3578 ns** |         **13.351 ns** |         **-** |
| **DivByWord**     | **1**    | **native**  |         **15.340 ns** |       **0.3940 ns** |         **1.1618 ns** |         **15.306 ns** |         **-** |
| **DivByWord**     | **4**    | **managed** |         **50.742 ns** |       **1.1417 ns** |         **3.3483 ns** |         **50.984 ns** |         **-** |
| **DivByWord**     | **4**    | **native**  |         **23.891 ns** |       **0.6724 ns** |         **1.9509 ns** |         **23.895 ns** |         **-** |
| **DivByWord**     | **16**   | **managed** |        **209.071 ns** |       **4.1929 ns** |        **10.0458 ns** |        **207.771 ns** |         **-** |
| **DivByWord**     | **16**   | **native**  |         **78.854 ns** |       **1.8473 ns** |         **5.4179 ns** |         **79.137 ns** |         **-** |
| **DivByWord**     | **64**   | **managed** |        **849.796 ns** |      **16.8479 ns** |        **42.2680 ns** |        **846.656 ns** |         **-** |
| **DivByWord**     | **64**   | **native**  |        **327.946 ns** |       **6.5716 ns** |        **17.5409 ns** |        **327.914 ns** |         **-** |
| **DivByWord**     | **256**  | **managed** |      **3,229.343 ns** |      **64.2673 ns** |       **142.4119 ns** |      **3,222.510 ns** |         **-** |
| **DivByWord**     | **256**  | **native**  |      **1,293.731 ns** |      **24.7750 ns** |        **29.4929 ns** |      **1,293.407 ns** |         **-** |
| **DivByWord**     | **1024** | **managed** |     **13,222.915 ns** |     **259.9746 ns** |       **548.3747 ns** |     **13,252.268 ns** |         **-** |
| **DivByWord**     | **1024** | **native**  |      **5,142.832 ns** |      **76.8051 ns** |        **71.8435 ns** |      **5,143.571 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **FromDecString** | **1**    | **managed** |         **63.593 ns** |       **1.7641 ns** |         **5.2016 ns** |         **63.773 ns** |      **88 B** |
| **FromDecString** | **1**    | **native**  |         **55.459 ns** |       **1.4642 ns** |         **4.2245 ns** |         **54.621 ns** |         **-** |
| **FromDecString** | **4**    | **managed** |        **271.823 ns** |       **7.2763 ns** |        **21.4543 ns** |        **268.789 ns** |     **176 B** |
| **FromDecString** | **4**    | **native**  |        **151.208 ns** |       **3.5913 ns** |        **10.5891 ns** |        **149.532 ns** |         **-** |
| **FromDecString** | **16**   | **managed** |      **1,475.564 ns** |      **33.6168 ns** |        **99.1200 ns** |      **1,467.167 ns** |     **608 B** |
| **FromDecString** | **16**   | **native**  |        **689.486 ns** |      **15.7196 ns** |        **46.3495 ns** |        **694.389 ns** |         **-** |
| **FromDecString** | **64**   | **managed** |     **12,276.699 ns** |     **295.2246 ns** |       **865.8428 ns** |     **12,174.980 ns** |    **2192 B** |
| **FromDecString** | **64**   | **native**  |      **5,440.579 ns** |     **127.5447 ns** |       **374.0664 ns** |      **5,500.879 ns** |         **-** |
| **FromDecString** | **256**  | **managed** |    **202,672.213 ns** |   **3,839.3216 ns** |     **8,346.3698 ns** |    **202,210.498 ns** |    **8384 B** |
| **FromDecString** | **256**  | **native**  |     **64,462.168 ns** |   **1,306.6355 ns** |     **3,790.7867 ns** |     **64,220.428 ns** |         **-** |
| **FromDecString** | **1024** | **managed** |  **4,781,862.666 ns** |  **95,154.7845 ns** |   **185,592.1134 ns** |  **4,777,399.219 ns** |   **33010 B** |
| **FromDecString** | **1024** | **native**  |    **949,132.162 ns** |  **21,873.1398 ns** |    **64,493.4509 ns** |    **943,901.172 ns** |    **8336 B** |
|               |      |         |                   |                 |                   |                   |           |
| **Mult**          | **1**    | **managed** |         **23.064 ns** |       **0.8339 ns** |         **2.4588 ns** |         **22.722 ns** |         **-** |
| **Mult**          | **1**    | **native**  |         **17.835 ns** |       **1.1246 ns** |         **3.3158 ns** |         **18.519 ns** |         **-** |
| **Mult**          | **4**    | **managed** |        **117.707 ns** |       **2.3098 ns** |         **6.3618 ns** |        **116.410 ns** |         **-** |
| **Mult**          | **4**    | **native**  |         **35.008 ns** |       **0.7379 ns** |         **2.0074 ns** |         **34.471 ns** |         **-** |
| **Mult**          | **16**   | **managed** |      **1,920.268 ns** |     **103.5803 ns** |       **305.4088 ns** |      **1,793.431 ns** |         **-** |
| **Mult**          | **16**   | **native**  |        **425.899 ns** |       **9.4695 ns** |        **27.7725 ns** |        **418.775 ns** |         **-** |
| **Mult**          | **64**   | **managed** |     **28,617.715 ns** |     **720.7577 ns** |     **2,091.0489 ns** |     **27,936.035 ns** |         **-** |
| **Mult**          | **64**   | **native**  |      **7,204.292 ns** |     **141.6154 ns** |       **145.4286 ns** |      **7,215.700 ns** |         **-** |
| **Mult**          | **256**  | **managed** |    **709,705.934 ns** |   **9,534.9929 ns** |     **7,962.1509 ns** |    **707,105.859 ns** |         **-** |
| **Mult**          | **256**  | **native**  |    **101,993.096 ns** |   **2,024.2813 ns** |     **4,610.3133 ns** |    **101,004.871 ns** |         **-** |
| **Mult**          | **1024** | **managed** | **13,056,257.378 ns** | **446,820.8656 ns** | **1,296,308.4447 ns** | **12,597,648.438 ns** |       **6 B** |
| **Mult**          | **1024** | **native**  |  **1,507,609.375 ns** |  **29,365.0692 ns** |    **49,864.1201 ns** |  **1,499,632.812 ns** |       **2 B** |
|               |      |         |                   |                 |                   |                   |           |
| **ShiftLeft**     | **1**    | **managed** |          **8.885 ns** |       **0.5791 ns** |         **1.6894 ns** |          **8.240 ns** |         **-** |
| **ShiftLeft**     | **1**    | **native**  |         **15.691 ns** |       **0.5018 ns** |         **1.4718 ns** |         **15.690 ns** |         **-** |
| **ShiftLeft**     | **4**    | **managed** |         **15.514 ns** |       **0.4081 ns** |         **1.2032 ns** |         **15.494 ns** |         **-** |
| **ShiftLeft**     | **4**    | **native**  |         **18.390 ns** |       **0.5043 ns** |         **1.4709 ns** |         **18.549 ns** |         **-** |
| **ShiftLeft**     | **16**   | **managed** |         **29.908 ns** |       **0.8274 ns** |         **2.4397 ns** |         **30.088 ns** |         **-** |
| **ShiftLeft**     | **16**   | **native**  |         **31.359 ns** |       **0.7079 ns** |         **2.0538 ns** |         **31.553 ns** |         **-** |
| **ShiftLeft**     | **64**   | **managed** |         **93.869 ns** |       **2.3651 ns** |         **6.9365 ns** |         **93.954 ns** |         **-** |
| **ShiftLeft**     | **64**   | **native**  |         **83.571 ns** |       **1.9672 ns** |         **5.7385 ns** |         **84.236 ns** |         **-** |
| **ShiftLeft**     | **256**  | **managed** |        **347.084 ns** |      **10.4193 ns** |        **30.5581 ns** |        **343.902 ns** |         **-** |
| **ShiftLeft**     | **256**  | **native**  |        **300.595 ns** |       **5.9818 ns** |        **14.2163 ns** |        **300.240 ns** |         **-** |
| **ShiftLeft**     | **1024** | **managed** |      **1,428.454 ns** |      **28.3420 ns** |        **73.1599 ns** |      **1,430.774 ns** |         **-** |
| **ShiftLeft**     | **1024** | **native**  |      **1,245.076 ns** |      **24.8845 ns** |        **67.7002 ns** |      **1,249.236 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **Sub**           | **1**    | **managed** |         **16.501 ns** |       **1.2421 ns** |         **3.6625 ns** |         **14.534 ns** |         **-** |
| **Sub**           | **1**    | **native**  |         **12.281 ns** |       **0.2719 ns** |         **0.6970 ns** |         **12.223 ns** |         **-** |
| **Sub**           | **4**    | **managed** |         **21.789 ns** |       **0.5318 ns** |         **1.5595 ns** |         **21.420 ns** |         **-** |
| **Sub**           | **4**    | **native**  |         **18.580 ns** |       **0.4001 ns** |         **1.1416 ns** |         **18.347 ns** |         **-** |
| **Sub**           | **16**   | **managed** |         **46.027 ns** |       **1.3888 ns** |         **3.7783 ns** |         **45.179 ns** |         **-** |
| **Sub**           | **16**   | **native**  |         **40.904 ns** |       **0.9655 ns** |         **2.8163 ns** |         **40.823 ns** |         **-** |
| **Sub**           | **64**   | **managed** |        **227.326 ns** |       **6.8851 ns** |        **20.3009 ns** |        **227.514 ns** |         **-** |
| **Sub**           | **64**   | **native**  |        **104.031 ns** |       **3.0157 ns** |         **8.8920 ns** |        **103.902 ns** |         **-** |
| **Sub**           | **256**  | **managed** |        **876.724 ns** |      **32.8765 ns** |        **96.4210 ns** |        **865.081 ns** |         **-** |
| **Sub**           | **256**  | **native**  |        **429.236 ns** |      **13.2781 ns** |        **38.9422 ns** |        **433.643 ns** |         **-** |
| **Sub**           | **1024** | **managed** |      **3,309.241 ns** |      **87.0835 ns** |       **256.7677 ns** |      **3,301.847 ns** |         **-** |
| **Sub**           | **1024** | **native**  |      **1,459.871 ns** |      **29.0760 ns** |        **80.0839 ns** |      **1,459.206 ns** |         **-** |
|               |      |         |                   |                 |                   |                   |           |
| **ToDecString**   | **1**    | **managed** |         **93.403 ns** |       **1.8829 ns** |         **3.7603 ns** |         **93.192 ns** |      **32 B** |
| **ToDecString**   | **1**    | **native**  |         **86.523 ns** |       **1.8065 ns** |         **5.2696 ns** |         **85.398 ns** |         **-** |
| **ToDecString**   | **4**    | **managed** |        **436.168 ns** |       **8.7374 ns** |        **19.3614 ns** |        **431.382 ns** |      **56 B** |
| **ToDecString**   | **4**    | **native**  |        **298.434 ns** |       **6.6722 ns** |        **19.4632 ns** |        **295.642 ns** |         **-** |
| **ToDecString**   | **16**   | **managed** |      **2,860.353 ns** |      **49.4525 ns** |        **41.2951 ns** |      **2,850.253 ns** |     **152 B** |
| **ToDecString**   | **16**   | **native**  |      **1,592.588 ns** |      **32.4277 ns** |        **95.1047 ns** |      **1,586.377 ns** |         **-** |
| **ToDecString**   | **64**   | **managed** |     **29,725.803 ns** |     **588.9863 ns** |     **1,134.7767 ns** |     **29,408.228 ns** |     **536 B** |
| **ToDecString**   | **64**   | **native**  |     **13,960.829 ns** |     **249.5966 ns** |       **306.5270 ns** |     **13,978.373 ns** |         **-** |
| **ToDecString**   | **256**  | **managed** |    **622,096.662 ns** |  **12,408.2164 ns** |    **29,489.4412 ns** |    **618,281.641 ns** |    **2072 B** |
| **ToDecString**   | **256**  | **native**  |    **189,427.626 ns** |   **3,758.4128 ns** |     **8,406.2293 ns** |    **187,542.480 ns** |         **-** |
| **ToDecString**   | **1024** | **managed** |  **9,650,509.125 ns** | **192,975.6607 ns** |   **389,820.3814 ns** |  **9,654,189.844 ns** |    **8222 B** |
| **ToDecString**   | **1024** | **native**  |  **2,854,979.118 ns** |  **56,601.0243 ns** |   **137,774.7260 ns** |  **2,815,651.758 ns** |       **2 B** |
