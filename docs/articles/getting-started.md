# Getting started

SimdBase64 is a small, dependency-free C# library that decodes base64 with SIMD
instructions. It targets **.NET 10** (or better) and runs on x64 and ARM64.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) or newer.
- A 64-bit x64 or ARM64 CPU for the SIMD kernels (a portable scalar fallback covers everything else).

## Build &amp; reference

Clone the repository and build the library:

```bash
git clone https://github.com/simdutf/SimdBase64.git
cd SimdBase64/src
dotnet build -c Release
```

Then add a project reference to `src/SimdBase64.csproj` from your own project:

```bash
dotnet add reference path/to/SimdBase64/src/SimdBase64.csproj
```

## Decoding a buffer

The core entry point is [`Base64.DecodeFromBase64`](xref:SimdBase64.Base64). It decodes the
input into a buffer you supply and returns an
[`OperationStatus`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.operationstatus),
along with the number of input characters consumed and output bytes written.

Size the output buffer with [`MaximalBinaryLengthFromBase64`](xref:SimdBase64.Base64):

```csharp
using System.Buffers;
using SimdBase64;

string base64 = "SGVsbG8sIFdvcmxkIQ==";
byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64(base64.AsSpan())];

OperationStatus status = Base64.DecodeFromBase64(
    base64.AsSpan(), buffer,
    out int bytesConsumed,
    out int bytesWritten,
    isUrl: false);

if (status == OperationStatus.Done)
{
    ReadOnlySpan<byte> decoded = buffer.AsSpan(0, bytesWritten);
    // Encoding.UTF8.GetString(decoded) == "Hello, World!"
}
```

The library supports both `Span<byte>` (ASCII / UTF-8) and `Span<char>` (UTF-16) input. If you
have a C# `string`, get its `Span<char>` with `AsSpan()`. Allowable white space is skipped
following the [WHATWG forgiving-base64](https://infra.spec.whatwg.org/#forgiving-base64-decode)
rules.

### The return value and out parameters

| Result | Meaning |
|--------|---------|
| `OperationStatus.Done` | The whole input was decoded successfully. |
| `OperationStatus.InvalidData` | The input contains a character that cannot be part of valid base64. |
| `OperationStatus.NeedMoreData` | The input ends with a single trailing character (an incomplete group). |
| `bytesConsumed` | Number of input characters consumed (including skipped white space and padding). |
| `bytesWritten` | Number of decoded bytes written to the output buffer. |

## base64url

Pass `isUrl: true` to decode the URL- and filename-safe alphabet (`-` and `_` instead of
`+` and `/`):

```csharp
var status = Base64.DecodeFromBase64(input, buffer,
    out int consumed, out int written, isUrl: true);
```

## Replacing `Convert.FromBase64String`

The .NET runtime does **not** accelerate `Convert.FromBase64String`. SimdBase64 ships a
faster drop-in:

```csharp
// before
byte[] bytes = Convert.FromBase64String(s);

// after
byte[] bytes = SimdBase64.Base64.FromBase64String(s);
```

## Choosing a specific kernel

`DecodeFromBase64` dispatches to the fastest kernel your CPU supports. The architecture-specific
implementations live in nested namespaces (`SimdBase64.Arm`, `SimdBase64.AVX512`,
`SimdBase64.AVX2`, `SimdBase64.SSE`, `SimdBase64.Scalar`) and can be called directly — useful
for testing or pinning behaviour.

Continue to [How it works](how-it-works.md) or jump to the [API reference](xref:SimdBase64.Base64).
