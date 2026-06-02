# API reference

SimdBase64 exposes a small, focused public surface in the `SimdBase64` namespace.

## Core types

- [`SimdBase64.Base64`](xref:SimdBase64.Base64) — the main entry point. `DecodeFromBase64`
  decodes a `Span<byte>` (ASCII / UTF-8) or `Span<char>` (UTF-16) input, dispatching to the
  fastest kernel your CPU supports. `FromBase64String` is a drop-in replacement for
  `Convert.FromBase64String`, and `MaximalBinaryLengthFromBase64` sizes the output buffer.

The architecture-specific kernels live in nested namespaces and are selected automatically at
runtime — you normally never call them directly:

- `SimdBase64.Arm.Base64` — ARM64 NEON.
- `SimdBase64.AVX2.Base64` — x64 AVX2.
- `SimdBase64.SSE.Base64` — x64 SSSE3 / SSE4.2.
- `SimdBase64.Scalar.Base64` — portable fallback (also exposes the safe, length-checked variants).

The members below are generated directly from the source.
