# Contributing &amp; building

SimdBase64 is an open-source project under the
[MIT License](https://github.com/simdutf/SimdBase64/blob/main/LICENSE).
Contributions, issues, and benchmark reports from new hardware are welcome.

## Building the library

```bash
cd src
dotnet build
```

## Running the tests

```bash
dotnet test
```

List the available tests:

```bash
dotnet test --list-tests
```

Filter to a specific test:

```bash
dotnet test -c Release --filter DecodeBase64CasesScalar
```

## Running the benchmarks

```bash
cd benchmark
dotnet run -c Release
```

Filter to a single benchmark:

```bash
dotnet run -c Release --filter "SimdUnicodeBenchmarks.RealDataBenchmark.AVX2DecodingRealDataUTF8(FileName: \"data/email/\")"
```

On macOS or Linux you may want privileged mode for hardware counters:

```bash
cd benchmark
sudo dotnet run -c Release
```

The UTF-16 benchmarks are off by default; enable them with a category flag:

```bash
cd benchmark
dotnet run -c Release --anyCategories UTF16
```

## Code formatting

```bash
cd test
dotnet format
```

## Programming tips

Print the contents of a vector register:

```csharp
public static void ToString(Vector256<byte> v)
{
    Span<byte> b = stackalloc byte[32];
    v.CopyTo(b);
    Console.WriteLine(Convert.ToHexString(b));
}

public static void ToString(Vector128<byte> v)
{
    Span<byte> b = stackalloc byte[16];
    v.CopyTo(b);
    Console.WriteLine(Convert.ToHexString(b));
}
```

Convert an integer to a hex string with `$"0x{myVariable:X}"`.

## Performance notes

A few hard-won tips when working on the SIMD kernels:

- `Vector128.Shuffle` is **not** the same as `Ssse3.Shuffle`, nor is `Vector256.Shuffle`
  the same as `Avx2.Shuffle`. Prefer the architecture-specific intrinsics.
- `Vector512.Shuffle` is a full 64-byte permute; `Avx512BW.Shuffle` is lane-wise `VPSHUFB`.
  The Ice Lake kernel uses `Avx512Vbmi.PermuteVar64x8` / `PermuteVar64x8x2`.
- Likewise, `Vector128.Shuffle` differs from `AdvSimd.Arm64.VectorTableLookup`; use the latter on ARM.
- Avoid `stackalloc` arrays in class instances.
- Prefer `struct` over `class` to make thread-local data explicit.
- Dump JIT assembly with `DOTNET_JitDisasm=...` and gather profiling data with `dotnet run -c Release -- -p EP`.
  See [Viewing JIT disassembly and dumps](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/viewing-jit-dumps.md).

## Scientific references

- Wojciech Muła, Daniel Lemire, [*Base64 encoding and decoding at almost the speed of a memory copy*](https://arxiv.org/abs/1910.05109), Software: Practice and Experience 50 (2), 2020.
- Wojciech Muła, Daniel Lemire, [*Faster Base64 Encoding and Decoding using AVX2 Instructions*](https://arxiv.org/abs/1704.00605), ACM Transactions on the Web 12 (3), 2018.

## Further reading

- [WHATWG forgiving-base64 decode](https://infra.spec.whatwg.org/#forgiving-base64-decode)
- [base64 encoding with SIMD support (dotnet/runtime#27433)](https://github.com/dotnet/runtime/issues/27433)
- [.NET framework design guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/)
