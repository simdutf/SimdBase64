using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;

namespace SimdBase64
{
    namespace AVX512
    {
        public static partial class Base64
        {
            // Ice Lake (AVX-512 VBMI / VBMI2) kernels, ported from simdutf:
            // https://github.com/simdutf/simdutf/blob/master/src/icelake/icelake_base64.inl.cpp
            // Tables from _mm512_set_epi8 are reversed for Vector512.Create (lowest lane first).

            [StructLayout(LayoutKind.Sequential)]
            private struct Block64
            {
                public Vector512<byte> chunk0;
            }

            // First 48 bytes of a decoded 64-character block.
            private static readonly Vector512<byte> DecodeStoreMask = Vector512.Create(
                Vector256.Create(byte.MaxValue),
                Vector256.Create(
                    byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
                    byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
                    byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
                    byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
                    (byte)0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void LoadBlock(Block64* b, byte* src)
            {
                b->chunk0 = Avx512BW.LoadVector512(src);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void LoadBlock(Block64* b, char* src)
            {
                Vector512<short> m1 = Avx512BW.LoadVector512((short*)src);
                Vector512<short> m2 = Avx512BW.LoadVector512((short*)(src + 32));
                Vector512<byte> packed = Avx512BW.PackUnsignedSaturate(m1, m2);
                Vector512<long> laneOrder = Vector512.Create(0L, 2L, 4L, 6L, 1L, 3L, 5L, 7L);
                b->chunk0 = Avx512F.PermuteVar8x64(packed.AsInt64(), laneOrder).AsByte();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe ulong ToBase64Mask(bool base64Url, Block64* b, ref bool error)
            {
                Vector512<byte> input = b->chunk0;

                Vector512<byte> asciiSpaceTbl = Vector512.Create(
                    (byte)32, 0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 0, 12, 13, 0, 0,
                    32, 0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 0, 12, 13, 0, 0,
                    32, 0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 0, 12, 13, 0, 0,
                    32, 0, 0, 0, 0, 0, 0, 0, 0, 9, 10, 0, 12, 13, 0, 0);

                Vector512<byte> lookup0;
                Vector512<byte> lookup1;
                if (base64Url)
                {
                    lookup0 = Vector512.Create(
                        (sbyte)-1, -128, -128, -128, -128, -128, -128, -128, -128, -1, -1, -128, -128, -1, -128, -128,
                        -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128,
                        -1, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, 62, -128, -128,
                        52, 53, 54, 55, 56, 57, 58, 59, 60, 61, -128, -128, -128, -128, -128, -128).AsByte();
                    lookup1 = Vector512.Create(
                        (sbyte)-128, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
                        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, -128, -128, -128, -128, 63,
                        -128, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
                        41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, -128, -128, -128, -128, -128).AsByte();
                }
                else
                {
                    lookup0 = Vector512.Create(
                        (sbyte)-128, -128, -128, -128, -128, -128, -128, -128, -128, -1, -1, -128, -128, -1, -128, -128,
                        -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128,
                        -1, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, 62, -128, -128, -128, 63,
                        52, 53, 54, 55, 56, 57, 58, 59, 60, 61, -128, -128, -128, -128, -128, -128).AsByte();
                    lookup1 = Vector512.Create(
                        (sbyte)-128, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
                        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, -128, -128, -128, -128, -128,
                        -128, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
                        41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, -128, -128, -128, -128, -128).AsByte();
                }

                Vector512<byte> translated = Avx512Vbmi.PermuteVar64x8x2(lookup0, input, lookup1);
                Vector512<byte> combined = Avx512F.Or(translated.AsInt64(), input.AsInt64()).AsByte();
                ulong mask = combined.ExtractMostSignificantBits();
                if (mask != 0)
                {
                    Vector512<byte> shuffled = Avx512BW.Shuffle(asciiSpaceTbl, input);
                    ulong spaces = Avx512BW.CompareEqual(shuffled, input).ExtractMostSignificantBits();
                    error |= (mask != spaces);
                }

                b->chunk0 = translated;
                return mask;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe ulong CompressBlock(ref Block64 b, ulong mask, byte* output)
            {
                ulong nmask = ~mask;
                Vector512<byte> keepMask = MaskFromUInt64(nmask);
                Vector512<byte> compressed = Avx512Vbmi2.Compress(Vector512<byte>.Zero, keepMask, b.chunk0);
                Avx512BW.Store(output, compressed);
                return Popcnt.X64.PopCount(nmask);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static Vector512<byte> MaskFromUInt64(ulong mask)
            {
                Vector256<byte> repeat = Vector256.Create(
                    (byte)0, 0, 0, 0, 0, 0, 0, 0,
                    1, 1, 1, 1, 1, 1, 1, 1,
                    2, 2, 2, 2, 2, 2, 2, 2,
                    3, 3, 3, 3, 3, 3, 3, 3);
                Vector256<byte> bits = Vector256.Create(
                    (byte)1, 2, 4, 8, 16, 32, 64, 128,
                    1, 2, 4, 8, 16, 32, 64, 128,
                    1, 2, 4, 8, 16, 32, 64, 128,
                    1, 2, 4, 8, 16, 32, 64, 128);
                Vector256<byte> loSrc = Vector256.Create(mask).AsByte();
                Vector256<byte> hiSrc = Vector256.Create(mask >> 32).AsByte();
                Vector256<byte> lo = Avx2.CompareEqual(Avx2.And(Avx2.Shuffle(loSrc, repeat), bits), bits);
                Vector256<byte> hi = Avx2.CompareEqual(Avx2.And(Avx2.Shuffle(hiSrc, repeat), bits), bits);
                return Vector512.Create(lo, hi);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void CopyBlock(Block64* b, byte* output)
            {
                Avx512BW.Store(output, b->chunk0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64Decode(byte* output, Vector512<byte> input)
            {
                Vector512<short> mergeAbAndBc = Avx512BW.MultiplyAddAdjacent(input, Vector512.Create(unchecked((int)0x01400140)).AsSByte());
                Vector512<int> merged = Avx512BW.MultiplyAddAdjacent(mergeAbAndBc, Vector512.Create(0x00011000).AsInt16());
                Vector512<byte> pack = Vector512.Create(
                    (byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 18, 17, 16, 22,
                    21, 20, 26, 25, 24, 30, 29, 28, 34, 33, 32, 38, 37, 36, 42, 41,
                    40, 46, 45, 44, 50, 49, 48, 54, 53, 52, 58, 57, 56, 62, 61, 60,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                Vector512<byte> shuffled = Avx512Vbmi.PermuteVar64x8(merged.AsByte(), pack);
                Avx512BW.MaskStore(output, DecodeStoreMask, shuffled);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* outPtr, byte* srcPtr)
            {
                Base64Decode(outPtr, Avx512BW.LoadVector512(srcPtr));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* output, Block64* block)
            {
                Base64Decode(output, block->chunk0);
            }

            // Caller is responsible for checking that Avx512Vbmi2.IsSupported && Popcnt.X64.IsSupported
            public unsafe static OperationStatus DecodeFromBase64AVX512(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                if (isUrl)
                {
                    return InnerDecodeFromBase64AVX512Url(source, dest, out bytesConsumed, out bytesWritten);
                }
                else
                {
                    return InnerDecodeFromBase64AVX512Regular(source, dest, out bytesConsumed, out bytesWritten);
                }
            }

            private unsafe static OperationStatus InnerDecodeFromBase64AVX512Regular(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
            {
                // translation from ASCII to 6 bit values
                bool isUrl = false;
                bytesConsumed = 0;
                bytesWritten = 0;
                const int blocksSize = 6;
                // Should be 
                // Span<byte> buffer = stackalloc byte[blocksSize * 64];
                Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                // Define pointers within the fixed blocks
                fixed (byte* srcInit = source)
                fixed (byte* dstInit = dest)
                fixed (byte* startOfBuffer = buffer)
                {
                    byte* srcEnd = srcInit + source.Length;
                    byte* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    int whiteSpaces = 0;
                    int equalsigns = 0;

                    int bytesToProcess = source.Length;
                    // skip trailing spaces
                    while (bytesToProcess > 0 && SimdBase64.Scalar.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                    {
                        bytesToProcess--;
                        whiteSpaces++;
                    }

                    int equallocation = bytesToProcess; // location of the first padding character if any
                    if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                    {
                        bytesToProcess -= 1;
                        equalsigns++;
                        while (bytesToProcess > 0 && SimdBase64.Scalar.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                        {
                            bytesToProcess--;
                            whiteSpaces++;
                        }
                        if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                        {
                            equalsigns++;
                            bytesToProcess -= 1;
                        }
                    }


                    {
                        byte* bufferPtr = startOfBuffer;

                        ulong bufferBytesConsumed = 0;//Only used if there is an error
                        ulong bufferBytesWritten = 0;//Only used if there is an error

                        if (bytesToProcess >= 64)
                        {
                            byte* srcEnd64 = srcInit + bytesToProcess - 64;
                            while (src <= srcEnd64)
                            {

                                Base64.Block64 b;
                                Base64.LoadBlock(&b, src);
                                src += 64;
                                bufferBytesConsumed += 64;
                                bool error = false;
                                UInt64 badCharMask = Base64.ToBase64Mask(isUrl, &b, ref error);
                                if (error == true)
                                {
                                    src -= bufferBytesConsumed;
                                    dst -= bufferBytesWritten;

                                    bytesConsumed = Math.Max(0, (int)(src - srcInit));
                                    bytesWritten = Math.Max(0, (int)(dst - dstInit));

                                    int remainderBytesConsumed = 0;
                                    int remainderBytesWritten = 0;

                                    OperationStatus result =
                                        SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                    bytesConsumed += remainderBytesConsumed;
                                    bytesWritten += remainderBytesWritten;
                                    return result;
                                }
                                if (badCharMask != 0)
                                {
                                    // optimization opportunity: check for simple masks like those made of
                                    // continuous 1s followed by continuous 0s. And masks containing a
                                    // single bad character.
                                    ulong compressedBytesCount = CompressBlock(ref b, badCharMask, bufferPtr);
                                    bufferPtr += compressedBytesCount;
                                    bufferBytesConsumed += compressedBytesCount;

                                }
                                else if (bufferPtr != startOfBuffer)
                                {
                                    CopyBlock(&b, bufferPtr);
                                    bufferPtr += 64;
                                    bufferBytesConsumed += 64;
                                }
                                else
                                {
                                    Base64DecodeBlock(dst, &b);
                                    bufferBytesWritten += 48;
                                    dst += 48;
                                }

                                if (bufferPtr >= (blocksSize - 1) * 64 + startOfBuffer) // We treat the last block separately later on
                                {
                                    for (int i = 0; i < (blocksSize - 2); i++) // We also treat the second to last block differently! Until then it is safe to proceed:
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + i * 64);
                                        bufferBytesWritten += 48;
                                        dst += 48;
                                    }
                                    Base64DecodeBlock(dst, startOfBuffer + (blocksSize - 2) * 64);

                                    dst += 48;
                                    Buffer.MemoryCopy(startOfBuffer + (blocksSize - 1) * 64, startOfBuffer, 64, 64);
                                    bufferPtr -= (blocksSize - 1) * 64;

                                    bufferBytesWritten = 0;
                                    bufferBytesConsumed = 0;
                                }

                            }
                        }
                        // Optimization note: if this is almost full, then it is worth our
                        // time, otherwise, we should just decode directly.

                        int lastBlock = (int)((bufferPtr - startOfBuffer) % 64);
                        int lastBlockSrcCount = 0;
                        // There is at some bytes remaining beyond the last 64 bit block remaining
                        if (lastBlock != 0 && srcEnd - src + lastBlock >= 64) // We first check if there is any error and eliminate white spaces?:
                        {
                            while ((bufferPtr - startOfBuffer) % 64 != 0 && src < srcEnd)
                            {
                                byte val = SimdBase64.Tables.GetToBase64Value((uint)*src);
                                *bufferPtr = val;
                                if (val > 64)
                                {
                                    bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                    bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                    int remainderBytesConsumed = 0;
                                    int remainderBytesWritten = 0;

                                    OperationStatus result =
                                        SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                    bytesConsumed += remainderBytesConsumed;
                                    bytesWritten += remainderBytesWritten;
                                    return result;
                                }
                                bufferPtr += (val <= 63) ? 1 : 0;
                                src++;
                                lastBlockSrcCount++;
                            }

                        }

                        byte* subBufferPtr = startOfBuffer;
                        for (; subBufferPtr + 64 <= bufferPtr; subBufferPtr += 64)
                        {

                            Base64DecodeBlock(dst, subBufferPtr);
                            dst += 48; // 64 bits of base64 decodes to 48 bits
                        }
                        if ((bufferPtr - subBufferPtr) % 64 != 0)
                        {
                            while (subBufferPtr + 4 < bufferPtr) // we decode one base64 element (4 bit) at a time
                            {

                                UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                    << 8;
                                triple = BinaryPrimitives.ReverseEndianness(triple);
                                Buffer.MemoryCopy(&triple, dst, 4, 4);
                                dst += 3;
                                subBufferPtr += 4;
                            }
                            if (subBufferPtr + 4 <= bufferPtr) // this may be the very last element, might be incomplete
                            {
                                UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                    << 8;
                                triple = BinaryPrimitives.ReverseEndianness(triple);
                                Buffer.MemoryCopy(&triple, dst, 3, 3);
                                dst += 3;
                                subBufferPtr += 4;
                            }
                            int leftover = (int)(bufferPtr - subBufferPtr);
                            if (leftover > 0)
                            {
                                while (leftover < 4 && src < srcEnd)
                                {
                                    byte val = SimdBase64.Tables.GetToBase64Value((uint)*src);
                                    if (val > 64)
                                    {
                                        bytesConsumed = (int)(src - srcInit);
                                        bytesWritten = (int)(dst - dstInit);
                                        return OperationStatus.InvalidData;
                                    }
                                    subBufferPtr[leftover] = (byte)(val);
                                    leftover += (val <= 63) ? 1 : 0;
                                    src++;
                                }

                                if (leftover == 1)
                                {

                                    bytesConsumed = (int)(src - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.NeedMoreData;
                                }
                                if (leftover == 2)
                                {
                                    UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                    ((UInt32)(subBufferPtr[1]) << 2 * 6);
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 1, 1);
                                    dst += 1;
                                }
                                else if (leftover == 3)
                                {
                                    UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                    ((UInt32)(subBufferPtr[1]) << 2 * 6) +
                                                    ((UInt32)(subBufferPtr[2]) << 1 * 6);
                                    triple = BinaryPrimitives.ReverseEndianness(triple);

                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                    dst += 2;
                                }
                                else
                                {
                                    UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                        << 8;
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    Buffer.MemoryCopy(&triple, dst, 3, 3);
                                    dst += 3;
                                }
                            }
                        }

                        if (src < srcEnd + equalsigns) // We finished processing 64-bit blocks, we're not quite at the end yet
                        {
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);

                            int remainderBytesConsumed = 0;
                            int remainderBytesWritten = 0;

                            OperationStatus result =
                                SimdBase64.Scalar.Base64.DecodeFromBase64Scalar(source.Slice(bytesConsumed, bytesToProcess - bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                            if (result == OperationStatus.InvalidData)
                            {
                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            else
                            {
                                bytesConsumed += remainderBytesConsumed + (source.Length - bytesToProcess);
                                bytesWritten += remainderBytesWritten;
                            }
                            if (result == OperationStatus.Done && equalsigns > 0)
                            {

                                // additional checks
                                if ((remainderBytesWritten % 3 == 0) || ((remainderBytesWritten % 3) + 1 + equalsigns != 4))
                                {
                                    result = OperationStatus.InvalidData;
                                }
                            }
                            return result;
                        }
                        if (equalsigns > 0) // final additional check
                        {
                            if (((int)(dst - dstInit) % 3 == 0) || (((int)(dst - dstInit) % 3) + 1 + equalsigns != 4))
                            {
                                return OperationStatus.InvalidData;
                            }
                        }

                        bytesConsumed = (int)(src - srcInit);
                        bytesWritten = (int)(dst - dstInit);
                        return OperationStatus.Done;
                    }

                }
            }

            private unsafe static OperationStatus InnerDecodeFromBase64AVX512Url(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
            {
                // translation from ASCII to 6 bit values
                bool isUrl = true;
                bytesConsumed = 0;
                bytesWritten = 0;
                const int blocksSize = 6;
                // Should be 
                // Span<byte> buffer = stackalloc byte[blocksSize * 64];
                Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                // Define pointers within the fixed blocks
                fixed (byte* srcInit = source)
                fixed (byte* dstInit = dest)
                fixed (byte* startOfBuffer = buffer)
                {
                    byte* srcEnd = srcInit + source.Length;
                    byte* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    int whiteSpaces = 0;
                    int equalsigns = 0;

                    int bytesToProcess = source.Length;
                    // skip trailing spaces
                    while (bytesToProcess > 0 && SimdBase64.Scalar.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                    {
                        bytesToProcess--;
                        whiteSpaces++;
                    }

                    int equallocation = bytesToProcess; // location of the first padding character if any
                    if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                    {
                        bytesToProcess -= 1;
                        equalsigns++;
                        while (bytesToProcess > 0 && SimdBase64.Scalar.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                        {
                            bytesToProcess--;
                            whiteSpaces++;
                        }
                        if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                        {
                            equalsigns++;
                            bytesToProcess -= 1;
                        }
                    }


                    {
                        byte* bufferPtr = startOfBuffer;

                        ulong bufferBytesConsumed = 0;//Only used if there is an error
                        ulong bufferBytesWritten = 0;//Only used if there is an error

                        if (bytesToProcess >= 64)
                        {
                            byte* srcEnd64 = srcInit + bytesToProcess - 64;
                            while (src <= srcEnd64)
                            {
                                Base64.Block64 b;
                                Base64.LoadBlock(&b, src);
                                src += 64;
                                bufferBytesConsumed += 64;
                                bool error = false;
                                UInt64 badCharMask = Base64.ToBase64Mask(isUrl, &b, ref error);
                                if (error == true)
                                {
                                    src -= bufferBytesConsumed;
                                    dst -= bufferBytesWritten;

                                    bytesConsumed = Math.Max(0, (int)(src - srcInit));
                                    bytesWritten = Math.Max(0, (int)(dst - dstInit));

                                    int remainderBytesConsumed = 0;
                                    int remainderBytesWritten = 0;

                                    OperationStatus result =
                                        SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                    bytesConsumed += remainderBytesConsumed;
                                    bytesWritten += remainderBytesWritten;
                                    return result;
                                }
                                if (badCharMask != 0)
                                {
                                    // optimization opportunity: check for simple masks like those made of
                                    // continuous 1s followed by continuous 0s. And masks containing a
                                    // single bad character.
                                    ulong compressedBytesCount = CompressBlock(ref b, badCharMask, bufferPtr);
                                    bufferPtr += compressedBytesCount;
                                    bufferBytesConsumed += compressedBytesCount;


                                }
                                else if (bufferPtr != startOfBuffer)
                                {
                                    CopyBlock(&b, bufferPtr);
                                    bufferPtr += 64;
                                    bufferBytesConsumed += 64;
                                }
                                else
                                {
                                    Base64DecodeBlock(dst, &b);
                                    bufferBytesWritten += 48;
                                    dst += 48;
                                }

                                if (bufferPtr >= (blocksSize - 1) * 64 + startOfBuffer) // We treat the last block separately later on
                                {
                                    for (int i = 0; i < (blocksSize - 2); i++) // We also treat the second to last block differently! Until then it is safe to proceed:
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + i * 64);
                                        bufferBytesWritten += 48;
                                        dst += 48;
                                    }
                                    Base64DecodeBlock(dst, startOfBuffer + (blocksSize - 2) * 64);



                                    dst += 48;
                                    Buffer.MemoryCopy(startOfBuffer + (blocksSize - 1) * 64, startOfBuffer, 64, 64);
                                    bufferPtr -= (blocksSize - 1) * 64;

                                    bufferBytesWritten = 0;
                                    bufferBytesConsumed = 0;
                                }

                            }
                        }
                        // Optimization note: if this is almost full, then it is worth our
                        // time, otherwise, we should just decode directly.
                        int lastBlock = (int)((bufferPtr - startOfBuffer) % 64);
                        // There is at some bytes remaining beyond the last 64 bit block remaining
                        if (lastBlock != 0 && srcEnd - src + lastBlock >= 64) // We first check if there is any error and eliminate white spaces?:
                        {
                            int lastBlockSrcCount = 0;
                            while ((bufferPtr - startOfBuffer) % 64 != 0 && src < srcEnd)
                            {
                                byte val = Tables.GetToBase64UrlValue((byte)*src);
                                *bufferPtr = val;
                                if (val > 64)
                                {
                                    bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                    bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                    int remainderBytesConsumed = 0;
                                    int remainderBytesWritten = 0;

                                    OperationStatus result =
                                        SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                    bytesConsumed += remainderBytesConsumed;
                                    bytesWritten += remainderBytesWritten;
                                    return result;
                                }
                                bufferPtr += (val <= 63) ? 1 : 0;
                                src++;
                                lastBlockSrcCount++;
                            }
                        }

                        byte* subBufferPtr = startOfBuffer;
                        for (; subBufferPtr + 64 <= bufferPtr; subBufferPtr += 64)
                        {
                            Base64DecodeBlock(dst, subBufferPtr);

                            dst += 48;// 64 bits of base64 decodes to 48 bits
                        }
                        if ((bufferPtr - subBufferPtr) % 64 != 0)
                        {
                            while (subBufferPtr + 4 < bufferPtr) // we decode one base64 element (4 bit) at a time
                            {
                                UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                    << 8;
                                triple = BinaryPrimitives.ReverseEndianness(triple);
                                Buffer.MemoryCopy(&triple, dst, 4, 4);
                                dst += 3;
                                subBufferPtr += 4;
                            }
                            if (subBufferPtr + 4 <= bufferPtr) // this may be the very last element, might be incomplete
                            {
                                UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                    ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                    << 8;
                                triple = BinaryPrimitives.ReverseEndianness(triple);
                                Buffer.MemoryCopy(&triple, dst, 3, 3);
                                dst += 3;
                                subBufferPtr += 4;
                            }
                            int leftover = (int)(bufferPtr - subBufferPtr);
                            if (leftover > 0)
                            {

                                while (leftover < 4 && src < srcEnd)
                                {
                                    byte val = Tables.GetToBase64UrlValue((byte)*src);
                                    if (val > 64)
                                    {
                                        bytesConsumed = (int)(src - srcInit);
                                        bytesWritten = (int)(dst - dstInit);
                                        return OperationStatus.InvalidData;
                                    }
                                    subBufferPtr[leftover] = (byte)(val);
                                    leftover += (val <= 63) ? 1 : 0;
                                    src++;
                                }

                                if (leftover == 1)
                                {
                                    bytesConsumed = (int)(src - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.NeedMoreData;
                                }
                                if (leftover == 2)
                                {
                                    UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                    ((UInt32)(subBufferPtr[1]) << 2 * 6);
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 1, 1);
                                    dst += 1;
                                }
                                else if (leftover == 3)
                                {
                                    UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                    ((UInt32)(subBufferPtr[1]) << 2 * 6) +
                                                    ((UInt32)(subBufferPtr[2]) << 1 * 6);
                                    triple = BinaryPrimitives.ReverseEndianness(triple);

                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                    dst += 2;
                                }
                                else
                                {
                                    UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                        ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                        << 8;
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    Buffer.MemoryCopy(&triple, dst, 3, 3);
                                    dst += 3;
                                }
                            }
                        }

                        if (src < srcEnd + equalsigns) // We finished processing 64-bit blocks, we're not quite at the end yet
                        {
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);

                            int remainderBytesConsumed = 0;
                            int remainderBytesWritten = 0;

                            OperationStatus result =
                                SimdBase64.Scalar.Base64.DecodeFromBase64Scalar(source.Slice(bytesConsumed, bytesToProcess - bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);


                            if (result == OperationStatus.InvalidData)
                            {
                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            else
                            {
                                bytesConsumed += remainderBytesConsumed + (source.Length - bytesToProcess);
                                bytesWritten += remainderBytesWritten;
                            }
                            if (result == OperationStatus.Done && equalsigns > 0)
                            {

                                // additional checks
                                if ((remainderBytesWritten % 3 == 0) || ((remainderBytesWritten % 3) + 1 + equalsigns != 4))
                                {
                                    result = OperationStatus.InvalidData;
                                }
                            }
                            return result;
                        }
                        if (equalsigns > 0) // final additional check
                        {
                            if (((int)(dst - dstInit) % 3 == 0) || (((int)(dst - dstInit) % 3) + 1 + equalsigns != 4))
                            {
                                return OperationStatus.InvalidData;
                            }
                        }

                        bytesConsumed = (int)(src - srcInit);
                        bytesWritten = (int)(dst - dstInit);
                        return OperationStatus.Done;
                    }

                }
            }
        }
    }
}
