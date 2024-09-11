using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;

namespace SimdBase64
{
    namespace SSE
    {
        public static partial class Base64
        {
            /*
            // If needed for debugging, you can do the following:
            static string VectorToString(Vector128<byte> vector)
            {
                Span<byte> bytes = stackalloc byte[16];
                vector.CopyTo(bytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("X2") + " ");
                }
                return sb.ToString().TrimEnd();
            }*/

            [StructLayout(LayoutKind.Sequential)]
            private struct Block64
            {
                public Vector128<byte> chunk0;
                public Vector128<byte> chunk1;
                public Vector128<byte> chunk2;
                public Vector128<byte> chunk3;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void LoadBlock(Block64* b, byte* src)
            {
                b->chunk0 = Sse2.LoadVector128(src);
                b->chunk1 = Sse2.LoadVector128(src + 16);
                b->chunk2 = Sse2.LoadVector128(src + 32);
                b->chunk3 = Sse2.LoadVector128(src + 48);
            }

            private unsafe static void LoadBlock(Block64* b, char* src)
            {
                // Load 128 bits (16 chars, 32 bytes) at each step from the UTF-16 source
                var m1 = Sse2.LoadVector128((ushort*)src);
                var m2 = Sse2.LoadVector128((ushort*)(src + 8));
                var m3 = Sse2.LoadVector128((ushort*)(src + 16));
                var m4 = Sse2.LoadVector128((ushort*)(src + 24));
                var m5 = Sse2.LoadVector128((ushort*)(src + 32));
                var m6 = Sse2.LoadVector128((ushort*)(src + 40));
                var m7 = Sse2.LoadVector128((ushort*)(src + 48));
                var m8 = Sse2.LoadVector128((ushort*)(src + 56));

                // Pack 16-bit chars down to 8-bit chars, handling two __m128i at a time
                b->chunk0 = Sse2.PackUnsignedSaturate(m1.AsInt16(), m2.AsInt16()).AsByte();
                b->chunk1 = Sse2.PackUnsignedSaturate(m3.AsInt16(), m4.AsInt16()).AsByte();
                b->chunk2 = Sse2.PackUnsignedSaturate(m5.AsInt16(), m6.AsInt16()).AsByte();
                b->chunk3 = Sse2.PackUnsignedSaturate(m7.AsInt16(), m8.AsInt16()).AsByte();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe ulong ToBase64Mask(bool base64Url, Block64* b, ref bool error)
            {
                ulong m0 = ToBase64Mask(base64Url, ref b->chunk0, ref error);
                ulong m1 = ToBase64Mask(base64Url, ref b->chunk1, ref error);
                ulong m2 = ToBase64Mask(base64Url, ref b->chunk2, ref error);
                ulong m3 = ToBase64Mask(base64Url, ref b->chunk3, ref error);
                return m0 | (m1 << 16) | (m2 << 32) | (m3 << 48);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static ushort ToBase64Mask(bool base64Url, ref Vector128<byte> src, ref bool error)
            {
                Vector128<sbyte> asciiSpaceTbl = Vector128.Create(
                    0x20, 0x0, 0x0, 0x0,
                    0x0, 0x0, 0x0, 0x0,
                    0x0, 0x9, 0xa, 0x0,
                    0xc, 0xd, 0x0, 0x0
                );

                Vector128<sbyte> deltaAsso = base64Url
                    ? Vector128.Create(0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x0, 0x0, 0x0, 0x0, 0x0, 0xF, 0x0, 0xF)
                    : Vector128.Create(0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x0F);

                Vector128<byte> deltaValues = base64Url
                    ? Vector128.Create(0x0, 0x0, 0x0, 0x13, 0x4, 0xBF, 0xBF, 0xB9, 0xB9, 0x0, 0x11, 0xC3, 0xBF, 0xE0, 0xB9, 0xB9)
                    : Vector128.Create(0x00, 0x00, 0x00, 0x13, 0x04, 0xBF, 0xBF, 0xB9, 0xB9, 0x00, 0x10, 0xC3, 0xBF, 0xBF, 0xB9, 0xB9);

                Vector128<sbyte> checkAsso = base64Url
                    ? Vector128.Create(0xD, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x3, 0x7, 0xB, 0xE, 0xB, 0x6)
                    : Vector128.Create(0xD, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x3, 0x7, 0xB, 0xB, 0xB, 0xF);

                Vector128<byte> checkValues = base64Url
                    ? Vector128.Create(0x80, 0x80, 0x80, 0x80, 0xCF, 0xBF, 0xB6, 0xA6, 0xB5, 0xA1, 0x0, 0x80, 0x0, 0x80, 0x0, 0x80)
                    : Vector128.Create(0x80, 0x80, 0x80, 0x80, 0xCF, 0xBF, 0xD5, 0xA6, 0xB5, 0x86, 0xD1, 0x80, 0xB1, 0x80, 0x91, 0x80);

                Vector128<Int32> shifted = Sse2.ShiftRightLogical(src.AsInt32(), 3);

                Vector128<byte> deltaHash = Sse2.Average(Ssse3.Shuffle(deltaAsso,
                                                                        src.AsSByte()).
                                                                        AsByte(),
                                                        shifted.AsByte());
                Vector128<byte> checkHash = Sse2.Average(Ssse3.Shuffle(checkAsso,
                                                                        src.AsSByte()).
                                                                        AsByte(),
                                                        shifted.AsByte());


                Vector128<sbyte> outVector = Sse2.AddSaturate(Ssse3.Shuffle(deltaValues.AsByte(), deltaHash).AsSByte(),
                                                            src.AsSByte());
                Vector128<sbyte> chkVector = Sse2.AddSaturate(Ssse3.Shuffle(checkValues.AsByte(), checkHash).AsSByte(),
                                                            src.AsSByte());

                int mask = Sse2.MoveMask(chkVector.AsByte());
                if (mask != 0)
                {
                    Vector128<byte> asciiSpace = Sse2.CompareEqual(Ssse3.Shuffle(asciiSpaceTbl.AsByte(), src), src);
                    error |= (mask != Sse2.MoveMask(asciiSpace));

                }

                src = outVector.AsByte();
                return (ushort)mask;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private unsafe static ulong CompressBlock(ref Block64 b, ulong mask, byte* output, byte* tablePtr)
            {
                ulong nmask = ~mask;
                Compress(b.chunk0, (ushort)mask, output, tablePtr);
                Compress(b.chunk1, (ushort)(mask >> 16), output + Popcnt.X64.PopCount(nmask & 0xFFFF), tablePtr);
                Compress(b.chunk2, (ushort)(mask >> 32), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFF), tablePtr);
                Compress(b.chunk3, (ushort)(mask >> 48), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFFFFFFUL), tablePtr);

                return Popcnt.X64.PopCount(nmask);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Compress(Vector128<byte> data, ushort mask, byte* output, byte* tablePtr)
            {
                if (mask == 0)
                {
                    Sse2.Store(output, data);
                    return;
                }

                // this particular implementation was inspired by work done by @animetosho
                // we do it in two steps, first 8 bytes and then second 8 bytes
                byte mask1 = (byte)mask;      // least significant 8 bits
                byte mask2 = (byte)(mask >> 8); // most significant 8 bits
                                                // next line just loads the 64-bit values thintable_epi8[mask1] and
                                                // thintable_epi8[mask2] into a 128-bit register, using only
                                                // two instructions on most compilers.

                ulong value1 = Tables.GetThintableEpi8(mask1);
                ulong value2 = Tables.GetThintableEpi8(mask2);

                Vector128<sbyte> shufmask = Vector128.Create(value2, value1).AsSByte();

                // Increment by 0x08 the second half of the mask
                shufmask = Sse2.Add(shufmask, Vector128.Create(0x08080808, 0x08080808, 0, 0).AsSByte());

                // this is the version "nearly pruned"
                Vector128<sbyte> pruned = Ssse3.Shuffle(data.AsSByte(), shufmask);
                // we still need to put the two halves together.
                // we compute the popcount of the first half:
                int pop1 = Tables.GetBitsSetTable256mul2(mask1);
                // then load the corresponding mask, what it does is to write
                // only the first pop1 bytes from the first 8 bytes, and then
                // it fills in with the bytes from the second 8 bytes + some filling
                // at the end.

                Vector128<byte> compactmask = Sse2.LoadVector128(tablePtr + pop1 * 8);

                Vector128<byte> answer = Ssse3.Shuffle(pruned.AsByte(), compactmask);
                Sse2.Store(output, answer);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void CopyBlock(Block64* b, byte* output)
            {
                // Directly store each 128-bit chunk to the output buffer using SSE2
                Sse2.Store(output, b->chunk0);
                Sse2.Store(output + 16, b->chunk1);
                Sse2.Store(output + 32, b->chunk2);
                Sse2.Store(output + 48, b->chunk3);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlockSafe(byte* outPtr, Block64* b)
            {
                Base64Decode(outPtr, b->chunk0);
                Base64Decode(outPtr + 12, b->chunk1);
                Base64Decode(outPtr + 24, b->chunk2);
                // should be:
                //Span<byte> buffer = stackalloc byte[16];
                Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

                // Safe memory copy for the last part of the data
                fixed (byte* bufferStart = buffer)
                {
                    Base64Decode(bufferStart, b->chunk3);
                    Buffer.MemoryCopy(bufferStart, outPtr + 36, 12, 12);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private unsafe static void Base64Decode(byte* output, Vector128<byte> input)
            {
                // credit: aqrit
                Vector128<sbyte> packShuffle = Vector128.Create(2, 1, 0, 6,
                                                                5, 4, 10, 9,
                                                                8, 14, 13, 12,
                                                               -1, -1, -1, -1);

                // Perform the initial multiply and add operation across unsigned 8-bit integers.
                Vector128<short> t0 = Ssse3.MultiplyAddAdjacent(input, Vector128.Create((Int32)0x01400140).AsSByte());

                // Perform another multiply and add to finalize the byte positions.
                Vector128<int> t1 = Sse2.MultiplyAddAdjacent(t0, Vector128.Create((Int32)0x00011000).AsInt16());

                // Shuffle the bytes according to the packShuffle pattern.
                Vector128<byte> t2 = Ssse3.Shuffle(t1.AsSByte(), packShuffle).AsByte();

                // Store the output. This writes 16 bytes, but we only need 12.
                Sse2.Store(output, t2);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* outPtr, byte* srcPtr)
            {
                Base64Decode(outPtr, Sse2.LoadVector128(srcPtr));
                Base64Decode(outPtr + 12, Sse2.LoadVector128(srcPtr + 16));
                Base64Decode(outPtr + 24, Sse2.LoadVector128(srcPtr + 32));
                Base64Decode(outPtr + 36, Sse2.LoadVector128(srcPtr + 48));
            }

            // Function to decode a Base64 block into binary data.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* output, Block64* block)
            {
                Base64Decode(output, block->chunk0);
                Base64Decode(output + 12, block->chunk1);
                Base64Decode(output + 24, block->chunk2);
                Base64Decode(output + 36, block->chunk3);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlockSafe(byte* outPtr, byte* srcPtr)
            {
                Base64Decode(outPtr, Sse2.LoadVector128(srcPtr));
                Base64Decode(outPtr + 12, Sse2.LoadVector128(srcPtr + 16));
                Base64Decode(outPtr + 24, Sse2.LoadVector128(srcPtr + 32));
                Vector128<byte> tempBlock = Sse2.LoadVector128(srcPtr + 48);
                // should be:
                // Span<byte> buffer = stackalloc byte[16];
                Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
                fixed (byte* bufferPtr = buffer)
                {
                    Base64Decode(bufferPtr, tempBlock);

                    // Copy only the first 12 bytes of the decoded fourth block into the output buffer, offset by 36 bytes.
                    // This step is necessary because the fourth block may not need all 16 bytes if it contains padding characters.
                    Buffer.MemoryCopy(bufferPtr, outPtr + 36, 12, 12);// DEGUG:Uncomment
                }
            }

            // Caller is responsible for checking that Ssse3.IsSupported && Popcnt.IsSupported
            public unsafe static OperationStatus DecodeFromBase64SSE(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {


                if (isUrl)
                {
                    return InnerDecodeFromBase64SSEUrl(source, dest, out bytesConsumed, out bytesWritten);
                }
                else
                {
                    return InnerDecodeFromBase64SSERegular(source, dest, out bytesConsumed, out bytesWritten);
                }
            }

            private unsafe static OperationStatus InnerDecodeFromBase64SSERegular(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
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
                fixed (byte* tablePtr = Tables.pshufbCombineTable)
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

                    // round up to the nearest multiple of 4, then multiply by 3
                    int decoded3bitsChunksToProcess = (bytesToProcess + 3) / 4 * 3;

                    byte* endOfSafe64ByteZone =
                        decoded3bitsChunksToProcess >= 63 ?
                                dst + decoded3bitsChunksToProcess - 63 :
                                dst;

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
                                    ulong compressedBytesCount = CompressBlock(ref b, badCharMask, bufferPtr, tablePtr);
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
                                    if (dst >= endOfSafe64ByteZone)
                                    {
                                        Base64DecodeBlockSafe(dst, &b);
                                    }
                                    else
                                    {
                                        Base64DecodeBlock(dst, &b);
                                    }
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
                                    if (dst >= endOfSafe64ByteZone) // for the second to last block, we may need to chcek if its unsafe to proceed
                                    {
                                        Base64DecodeBlockSafe(dst, startOfBuffer + (blocksSize - 2) * 64);
                                    }
                                    else
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + (blocksSize - 2) * 64);
                                    }



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
                            if (dst >= endOfSafe64ByteZone)
                            {
                                Base64DecodeBlockSafe(dst, subBufferPtr);
                            }
                            else
                            {
                                Base64DecodeBlock(dst, subBufferPtr);
                            }

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

            private unsafe static OperationStatus InnerDecodeFromBase64SSEUrl(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
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
                fixed (byte* tablePtr = Tables.pshufbCombineTable)
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

                    // round up to the nearest multiple of 4, then multiply by 3
                    int decoded3bitsChunksToProcess = (bytesToProcess + 3) / 4 * 3;

                    byte* endOfSafe64ByteZone =
                        decoded3bitsChunksToProcess >= 63 ?
                                dst + decoded3bitsChunksToProcess - 63 :
                                dst;

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
                                    ulong compressedBytesCount = CompressBlock(ref b, badCharMask, bufferPtr, tablePtr);
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
                                    if (dst >= endOfSafe64ByteZone)
                                    {
                                        Base64DecodeBlockSafe(dst, &b);
                                    }
                                    else
                                    {
                                        Base64DecodeBlock(dst, &b);
                                    }
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
                                    if (dst >= endOfSafe64ByteZone) // for the second to last block, we may need to chcek if its unsafe to proceed
                                    {
                                        Base64DecodeBlockSafe(dst, startOfBuffer + (blocksSize - 2) * 64);
                                    }
                                    else
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + (blocksSize - 2) * 64);
                                    }



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
                            if (dst >= endOfSafe64ByteZone)
                            {
                                Base64DecodeBlockSafe(dst, subBufferPtr);
                            }
                            else
                            {
                                Base64DecodeBlock(dst, subBufferPtr);
                            }

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
                                bytesConsumed += remainderBytesConsumed + (source.Length - bytesToProcess);
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
