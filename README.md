# SimdBase64
## Fast WHATWG forgiving-base64 in C#

The C# standard library has fast (SIMD-based) base64 encoding functions, but it lacks
base64 decoding function. The initial work that lead to the fast functions in the runtime
was carried out by [gfoidl](https://github.com/gfoidl/Base64).

The goal of this project is to provide the fast WHATWG forgiving-base64 algorithm already
used in major JavaScript runtimes (Node.js and Bun) to C#. It would complete the existing work.


## Requirements

We recommend you install .NET 8 or better: https://dotnet.microsoft.com/en-us/download/dotnet/8.0


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
dotnet run --configuration Release --filter "*somefilter*"
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

## Performance tips

- Be careful: `Vector128.Shuffle` is not the same as `Ssse3.Shuffle` nor is  `Vector256.Shuffle` the same as `Avx2.Shuffle`. Prefer the latter.
- Similarly `Vector128.Shuffle` is not the same as `AdvSimd.Arm64.VectorTableLookup`, use the latter.
- `stackalloc` arrays should probably not be used in class instances.
- In C#, `struct` might be preferable to `class` instances as it makes it clear that the data is thread local.
- You can ask for an asm dump: `DOTNET_JitDisasm=NEON64HTMLScan dotnet run -c Release`.  See [Viewing JIT disassembly and dumps](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/jit/viewing-jit-dumps.md).


## References

- [base64 encoding with simd-support](https://github.com/dotnet/runtime/issues/27433)
- [gfoidl.Base64](https://github.com/gfoidl/Base64): original code that lead to the SIMD-based code in the runtime
- [simdutf's base64 decode](https://github.com/simdutf/simdutf/blob/74126531454de9b06388cb2de78b18edbfcfbe3d/src/westmere/sse_base64.cpp#L337)
- [WHATWG forgiving-base64 decode](https://infra.spec.whatwg.org/#forgiving-base64-decode)

## More reading 

- https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
- https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
