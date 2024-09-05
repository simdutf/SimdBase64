# SimdBase64
## Fast WHATWG forgiving-base64 in C#

The C# standard library has fast (SIMD-based) base64 encoding functions, but it lacks
really fast base64 decoding function. The initial work that lead to the fast functions in the runtime
was carried out by [gfoidl](https://github.com/gfoidl/Base64). 

-  There are accelerated base64 functions for UTF-8 inputs in the .NET runtime, but they are not optimal: 
we can make them 50% faster.
- There is no accelerated base64 functions for UTF-16 inputs (e.g., `string` types). We can be 2x faster
or more.

The goal of this project is to provide the fast WHATWG forgiving-base64 algorithm already
used in major JavaScript runtimes (Node.js and Bun) to C#.

Importantly, we only focus on base64 decoding. It is a more challenging problem than base64 encoding because
of the presence of allowable white space characters and the need to validate the input. Indeed, all
inputs are valid for encoding, but only some inputs are valid for decoding. Having to skip white space 
characters makes accelerated decoding somewhat difficult.

## Results (SimdBase64 vs. fast .NET functions)

We use the enron base64 data for benchmarking, see benchmark/data/email.
We process the data as UTF-8 (ASCII) using the .NET accelerated functions
as a reference (`System.Buffers.Text.Base64.DecodeFromUtf8`).


| processor       | SimdBase64(GB/s) | .NET speed (GB/s) | speed up |
|:----------------|:------------------------|:-------------------|:-------------------|
| Apple M2 processor (ARM)   | 6.3                      | 3.8               | 1.6 x |
| Intel Ice Lake (AVX2)   | 5.3                      | 3.4              | 1.6 x |

Our results are more impressive when comparing against the standard base64 string decoding
function (`Convert.FromBase64String(mystring)`), but we omit these results for simplicity.

## Requirements

We require .NET 9 or better: https://dotnet.microsoft.com/en-us/download/dotnet/9.0


## Usage

The library only provides Base64 decoding functions, because the .NET library already has
fast Base64 encoding functions.

```c#
        string base64 = "SGVsbG8sIFdvcmxkIQ==";
        byte[] buffer = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64(base64.AsSpan())];
        int bytesConsumed; // gives you the number of characters consumed
        int bytesWritten;
        var result = SimdBase64.Base64.DecodeFromBase64(base64.AsSpan(), buffer, out bytesConsumed, out bytesWritten, false); // false is for regular base64, true for base64url
        // result == OperationStatus.Done
        // Encoding.UTF8.GetString(buffer.AsSpan().Slice(0, bytesWritten)) == "Hello, World!"

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
