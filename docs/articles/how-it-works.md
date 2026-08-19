# How it works

## The problem

Base64 represents arbitrary binary data as ASCII text. It is part of the email standard (MIME)
and is widely used to embed data in XML, HTML and JSON — images, cryptographic keys, and more.

Encoding is the easy direction: every input is valid. Decoding is harder. A decoder has to
**validate** the input (not every ASCII string is legal base64) and **skip allowable white
space**, which can appear anywhere. Those two requirements are exactly what makes a naïve,
byte-at-a-time decoder slow and branch-heavy. We implement the
[WHATWG forgiving-base64 decode](https://infra.spec.whatwg.org/#forgiving-base64-decode) rules.

## SIMD decoding

SimdBase64 implements the algorithm described in:

> Wojciech Muła, Daniel Lemire, [*Base64 encoding and decoding at almost the speed of a memory copy*](https://arxiv.org/abs/1910.05109), Software: Practice and Experience 50 (2), 2020.

The same approach is deployed in the [simdutf](https://github.com/simdutf/simdutf) C++ library
and used in production by the **Node.js** and **Bun** JavaScript runtimes. The key idea is to
process 16, 32, or 64 bytes at a time with SIMD lookup tables instead of per-character branches.

At a high level, each vectorized block:

1. Loads a block of base64 characters and translates each one to its 6-bit value with a
   shuffle-based table lookup.
2. Detects invalid characters and white space in parallel, compressing the white space out of
   the block so only meaningful characters remain.
3. Packs the 6-bit values down into the output bytes (4 base64 characters → 3 bytes).
4. Falls back to a careful scalar tail for the last partial block and any padding (`=`).

## Runtime dispatch

A single public method picks the best kernel for the host CPU, in priority order:

```text
ARM64 NEON  →  AVX-512 VBMI2  →  AVX2  →  SSSE3  →  scalar fallback
```

This means you write one call and automatically get NEON on an Apple M-series laptop, AVX-512
on Ice Lake and later x64 servers, AVX2 on older x64, and a correct scalar implementation
everywhere else.

| Back-end | Vector width | Typical hardware |
|----------|--------------|------------------|
| AVX-512 VBMI2 | 512-bit | Ice Lake, Sapphire Rapids, Emerald Rapids, Zen 4+ |
| AVX2 | 256-bit | Most current x64 |
| SSE4.2 / SSSE3 | 128-bit | Older x64 |
| ARM64 NEON | 128-bit | Apple Silicon, AWS Graviton, Snapdragon |
| Scalar | — | Portable fallback |

## AVX-512

On .NET 10 the VBMI / VBMI2 intrinsics (`PermuteVar64x8x2`, `Compress`) are available, so we
ship an Ice Lake kernel ported from [simdutf](https://github.com/simdutf/simdutf). It processes
64 input bytes per iteration, compresses white space with `VPCOMPRESSB`, and writes exactly 48
decoded bytes with a masked store.

## Why an `OperationStatus`, not a `bool`?

Returning an [`OperationStatus`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.operationstatus)
together with `bytesConsumed`/`bytesWritten` is strictly more informative than a boolean:
callers can tell a genuinely invalid input (`InvalidData`) from a merely truncated one
(`NeedMoreData`), report the exact offset where decoding stopped, and stream input in chunks.

See the [API reference](xref:SimdBase64.Base64) for every available kernel, and the
[benchmarks](benchmarks.md) for measured throughput.
