# SimdBase64
## Fast WHATWG forgiving base64 decoding in C#

Base64 is a standard approach to represent any binary data as ASCII. It is part of the email
standard (MIME) and is commonly used to embed data in XML, HTML or JSON files. For example,
images can be encoded as text using base64. Base64 is also used to represent cryptographic keys.

Our processors have SIMD instructions that are ideally suited to encode and decode base64.
Encoding is somewhat easier than decoding. Decoding is a more challenging problem than base64 encoding because
of the presence of allowable white space characters and the need to validate the input. Indeed, all
inputs are valid for encoding, but only some inputs are valid for decoding. Having to skip white space 
characters makes accelerated decoding somewhat difficult. We refer to this decoding as WHATWG forgiving-base64 decoding.

The C# standard library has fast (SIMD-based) base64 encoding functions. It also has fast decoding
functions. Yet these accelerated base64 decoding functions for UTF-8 inputs in the .NET runtime are not optimal: 
we beat them by 1.7 x to 2.3 x on inputs of a few kilobytes by using a novel different algorithm.
This fast WHATWG forgiving-base64 algorithm is already used in major JavaScript runtimes (Node.js and Bun).

A full description of the new algorithm will be published soon. The algorithm is unpatented (free) and we make our
C# code available under a liberal open-source licence (MIT).


## Results (SimdBase64 vs. fast .NET functions)

We use the enron base64 data for benchmarking, see benchmark/data/email.
We process the data as UTF-8 (ASCII) using the .NET accelerated functions
as a reference (`System.Buffers.Text.Base64.DecodeFromUtf8`).


| processor and base freq.      | SimdBase64 (GB/s) | .NET speed (GB/s) | speed up |
|:----------------|:------------------------|:-------------------|:-------------------|
| Apple M2 processor (ARM, 3.5 Ghz)   | 6.5                      | 3.8               | 1.7 x |
| AWS Graviton 3 (ARM, 2.6 GHz)   | 3.6  | 2.0 | 1.8 x |
| Intel Ice Lake (2.0 GHz)  | 6.5                      | 3.4              | 1.9 x |
| AMD EPYC 7R32 (Zen 2, 2.8 GHz)    |  6.8        | 2.9 | 2.3 x |


As an aside, there is no accelerated base64 functions for UTF-16 inputs (e.g., `string` types). 
We can multiply the decoding speed compared to the .NET standard library (`Convert.FromBase64String(mystring)`),
but we omit the numbers for simplicity.

## AVX-512

As for .NET 9, the support for AVX-512 remains incomplete in C#. In particular, important
VBMI2 instructions are missing. Hence, we are not using AVX-512 under x64 systems at this time.
However, as soon as .NET offers the necessary support, we will update our results.

## Requirements

We require .NET 9 or better: https://dotnet.microsoft.com/en-us/download/dotnet/9.0

## Usage

The library only provides Base64 decoding functions, because the .NET library already has
fast Base64 encoding functions. We support both `Span<byte>` (ASCII or UTF-8) and
`Span<char>` (UTF-16) as input. If you have C# string, you can get its `Span<char>` with
the `AsSpan()` method.

```c#
        string base64 = "SGVsbG8sIFdvcmxkIQ=="; // could be span<byte> in UTF-8 as well
        byte[] buffer = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64(base64.AsSpan())];
        int bytesConsumed; // gives you the number of characters consumed
        int bytesWritten;
        var result = SimdBase64.Base64.DecodeFromBase64(base64.AsSpan(), buffer, out bytesConsumed, out bytesWritten, false); // false is for regular base64, true for base64url
        // result == OperationStatus.Done
        var answer = buffer.AsSpan().Slice(0, bytesWritten); // decoded result
        // Encoding.UTF8.GetString(answer) == "Hello, World!"
```

## Running tests

```
dotnet test
```

To get a list of available tests, enter the command:

```
dotnet test --list-tests
```

To run specific tests, it is helpful to use the filter parameter:

```
dotnet test -c Release --filter DecodeBase64CasesScalar
```

## Running Benchmarks

To run the benchmarks, run the following command:
```
cd benchmark
dotnet run -c Release
```

To run just one benchmark, use a filter:

```
cd benchmark
dotnet run -c Release --filter "SimdUnicodeBenchmarks.RealDataBenchmark.AVX2DecodingRealDataUTF8(FileName: \"data/email/\")"
```

If you are under macOS or Linux, you may want to run the benchmarks in privileged mode:

```
cd benchmark
sudo dotnet run -c Release
```

## Building the library

```
cd src
dotnet build
```

## Code format

We recommend you use `dotnet format`. E.g.,

```
cd test
dotnet format
```

## Programming tips

You can print the content of a vector register like so:

```C#
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

You can convert an integer to a hex string like so: `$"0x{MyVariable:X}"`.

## Performance tips

- Be careful: `Vector128.Shuffle` is not the same as `Ssse3.Shuffle` nor is  `Vector256.Shuffle` the same as `Avx2.Shuffle`. Prefer the latter.
- Similarly `Vector128.Shuffle` is not the same as `AdvSimd.Arm64.VectorTableLookup`, use the latter.
- `stackalloc` arrays should probably not be used in class instances.
- In C#, `struct` might be preferable to `class` instances as it makes it clear that the data is thread local.
- You can ask for an asm dump: `DOTNET_JitDisasm=NEON64HTMLScan dotnet run -c Release`.  See [Viewing JIT disassembly and dumps](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/viewing-jit-dumps.md).

## Scientific References

- Wojciech Muła, Daniel Lemire, [Base64 encoding and decoding at almost the speed of a memory copy](https://arxiv.org/abs/1910.05109), Software: Practice and Experience 50 (2), 2020.
- Wojciech Muła, Daniel Lemire, [Faster Base64 Encoding and Decoding using AVX2 Instructions](https://arxiv.org/abs/1704.00605), ACM Transactions on the Web 12 (3), 2018.

## References

- [base64 encoding with simd-support](https://github.com/dotnet/runtime/issues/27433)
- [gfoidl.Base64](https://github.com/gfoidl/Base64): original code that lead to the SIMD-based code in the runtime
- [simdutf's base64 decode](https://github.com/simdutf/simdutf/blob/74126531454de9b06388cb2de78b18edbfcfbe3d/src/westmere/sse_base64.cpp#L337)
- [WHATWG forgiving-base64 decode](https://infra.spec.whatwg.org/#forgiving-base64-decode)

## More reading 

- https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
- https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
