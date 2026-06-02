---
_layout: landing
title: SimdBase64 — fast base64 decoding for .NET
---

<div class="hero">
  <div class="hero-inner">
    <h1 class="hero-title">SimdBase64</h1>
    <p class="hero-tagline">A blazing-fast C# library for WHATWG forgiving-base64 decoding — <strong>up to&nbsp;2.3&times; faster</strong> than the accelerated .NET functions and <strong>3.8&times;</strong> faster than <code>Convert.FromBase64String</code>, using AVX2, SSE and ARM&nbsp;NEON.</p>
    <div class="hero-cta">
      <a class="btn btn-primary" href="articles/getting-started.md">Get started &rarr;</a>
      <a class="btn btn-ghost" href="api/index.md">API reference</a>
      <a class="btn btn-ghost" href="https://github.com/simdutf/SimdBase64">GitHub</a>
    </div>
  </div>
</div>

<div class="stat-row">
  <div class="stat-card">
    <div class="stat-num">2.6&times;</div>
    <div class="stat-label">faster than accelerated .NET base64 (Apple M2)</div>
  </div>
  <div class="stat-card">
    <div class="stat-num">3.8&times;</div>
    <div class="stat-label">faster than <code>Convert.FromBase64String</code></div>
  </div>
  <div class="stat-card">
    <div class="stat-num">3</div>
    <div class="stat-label">SIMD back-ends: AVX2, SSE4.2, NEON</div>
  </div>
  <div class="stat-card">
    <div class="stat-num">0</div>
    <div class="stat-label">allocations — decodes directly into your buffer</div>
  </div>
</div>

## Drop-in replacement

SimdBase64 decodes base64 into a buffer you own. It accepts both `Span<byte>` (ASCII / UTF-8) and `Span<char>` (UTF-16) input, skips allowable white space, validates the input, and reports exactly how many bytes it consumed and wrote.

```csharp
using System.Buffers;
using SimdBase64;

string base64 = "SGVsbG8sIFdvcmxkIQ=="; // could be a Span<byte> of UTF-8 too
byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64(base64.AsSpan())];

OperationStatus status = Base64.DecodeFromBase64(
    base64.AsSpan(), buffer,
    out int bytesConsumed,
    out int bytesWritten,
    isUrl: false); // true for the base64url alphabet

// status == OperationStatus.Done
ReadOnlySpan<byte> answer = buffer.AsSpan(0, bytesWritten);
// Encoding.UTF8.GetString(answer) == "Hello, World!"
```

Already calling `Convert.FromBase64String`? Swap in the accelerated version with a one-line change:

```csharp
byte[] bytes = SimdBase64.Base64.FromBase64String(s);
```

The right SIMD kernel is selected automatically at runtime: **ARM64 NEON**, **AVX2**, **SSE4.2 / SSSE3**, or a portable scalar fallback.

<div class="feature-grid">
  <div class="feature">
    <div class="feature-icon">⚡</div>
    <h3>SIMD-accelerated</h3>
    <p>Decodes blocks of base64 at once with vector instructions — the same algorithm shipped in <a href="https://github.com/simdutf/simdutf">simdutf</a> and used by Node.js and Bun.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🧭</div>
    <h3>Runtime dispatch</h3>
    <p>One call, the best available kernel. AVX2, SSE4.2, ARM NEON or a scalar fallback — chosen for your CPU.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🧹</div>
    <h3>WHATWG forgiving</h3>
    <p>Handles allowable white space and padding, and validates the input — implementing the WHATWG forgiving-base64 decode rules.</p>
  </div>
  <div class="feature">
    <div class="feature-icon">🍏</div>
    <h3>x64 &amp; ARM</h3>
    <p>First-class support for modern Intel/AMD and Apple Silicon / Graviton processors.</p>
  </div>
</div>

## How fast is it?

Decoding throughput against the accelerated .NET functions (`System.Buffers.Text.Base64.DecodeFromUtf8`) on the enron email corpus. Longer bars are faster — SimdBase64 in purple, the .NET standard library in grey.

<div class="bench" data-unit="GB/s">
  <div class="bench-row"><span class="bench-name">Apple M2 (NEON)</span><div class="bench-bars"><div class="bar bar-simd" style="--v:100%"><span>10 GB/s</span></div><div class="bar bar-net" style="--v:38%"><span>3.8</span></div></div><span class="bench-x">2.6&times;</span></div>
  <div class="bench-row"><span class="bench-name">Intel Ice Lake</span><div class="bench-bars"><div class="bar bar-simd" style="--v:76%"><span>7.6 GB/s</span></div><div class="bar bar-net" style="--v:34%"><span>3.4</span></div></div><span class="bench-x">2.2&times;</span></div>
  <div class="bench-row"><span class="bench-name">AMD EPYC (Zen 2)</span><div class="bench-bars"><div class="bar bar-simd" style="--v:69%"><span>6.9 GB/s</span></div><div class="bar bar-net" style="--v:30%"><span>3.0</span></div></div><span class="bench-x">2.3&times;</span></div>
  <div class="bench-row"><span class="bench-name">AWS Graviton 3</span><div class="bench-bars"><div class="bar bar-simd" style="--v:51%"><span>5.1 GB/s</span></div><div class="bar bar-net" style="--v:20%"><span>2.0</span></div></div><span class="bench-x">2.6&times;</span></div>
</div>

<p class="bench-note">Against the unaccelerated <code>Convert.FromBase64String</code>, the gap is even larger — 3.6&times;–3.8&times;. See the full set of measurements in the <a href="articles/benchmarks.md">benchmarks</a>.</p>

## Build it

```bash
git clone https://github.com/simdutf/SimdBase64.git
cd SimdBase64/src
dotnet build -c Release
```

Then add a project reference to `src/SimdBase64.csproj`. Head to the [getting started guide](articles/getting-started.md) or dive into the [API reference](xref:SimdBase64.Base64).

## Citing

The algorithms are described in:

> Wojciech Muła, Daniel Lemire, [*Base64 encoding and decoding at almost the speed of a memory copy*](https://arxiv.org/abs/1910.05109), Software: Practice and Experience 50 (2), 2020.

> Wojciech Muła, Daniel Lemire, [*Faster Base64 Encoding and Decoding using AVX2 Instructions*](https://arxiv.org/abs/1704.00605), ACM Transactions on the Web 12 (3), 2018.
