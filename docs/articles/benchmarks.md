# Benchmarks

All numbers are base64 **decoding** throughput in **GB/s** (higher is better) measured with
[BenchmarkDotNet](https://benchmarkdotnet.org/) on the enron email corpus
(`benchmark/data/email`). The benchmarks are fully reproducible.

To reproduce on your own machine:

```bash
cd benchmark
dotnet run -c Release
# or a single benchmark:
dotnet run -c Release --filter "SimdUnicodeBenchmarks.RealDataBenchmark.AVX2DecodingRealDataUTF8(FileName: \"data/email/\")"
```

For the UTF-16 benchmarks (off by default):

```bash
cd benchmark
dotnet run -c Release --anyCategories UTF16
```

## vs. the accelerated .NET functions

Compared against `System.Buffers.Text.Base64.DecodeFromUtf8`, the runtime's fast SIMD base64
decoder. SimdBase64 is **1.7×–2.6×** faster on realistic inputs of a few kilobytes.

| processor and base freq.            | SimdBase64 (GB/s) | .NET (GB/s) | speed-up |
|:------------------------------------|:-----------------:|:-----------:|:--------:|
| Apple M2 (ARM, 3.5 GHz)             | 10  | 3.8 | 2.6× |
| AWS Graviton 3 (ARM, 2.6 GHz)       | 5.1 | 2.0 | 2.6× |
| Intel Ice Lake (2.0 GHz)            | 7.6 | 3.4 | 2.2× |
| AMD EPYC 7R32 (Zen 2, 2.8 GHz)      | 6.9 | 3.0 | 2.3× |

## vs. `Convert.FromBase64String`

The .NET runtime does **not** accelerate `Convert.FromBase64String`. Replacing it with
`SimdBase64.Base64.FromBase64String` multiplies the decoding speed:

| processor and base freq.            | SimdBase64 (GB/s) | .NET (GB/s) | speed-up |
|:------------------------------------|:-----------------:|:-----------:|:--------:|
| Apple M2 (ARM, 3.5 GHz)             | 4.0 | 1.1  | 3.6× |
| Intel Ice Lake (2.0 GHz)            | 2.5 | 0.65 | 3.8× |

> Hardware, runtime version and input all affect these numbers. Treat the tables as
> representative, and measure on your own target hardware for decisions that matter.
