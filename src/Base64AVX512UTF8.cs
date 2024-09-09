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

            // If needed for debugging, you can do the following:
            /*
            static string VectorToString(Vector512<byte> vector)
            {
                Span<byte> bytes = new byte[32];
                vector.CopyTo(bytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("X2") + " ");
                }
                return sb.ToString().TrimEnd();
            }

            static string VectorToStringChar(Vector512<byte> vector)
            {
                Span<byte> bytes = new byte[32];
                vector.CopyTo(bytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append((char)b);
                }
                return sb.ToString().TrimEnd();
            }
            */

            [StructLayout(LayoutKind.Sequential)]
            private struct Block64
            {
                public Vector512<byte> chunk0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void LoadBlock(Block64* b, byte* src)
            {
                b->chunk0 = Avx512F.LoadVector512(src);
            }

            private unsafe static void LoadBlock(Block64* b, char* src)
            {
                var m1 = Avx512F.LoadVector512((int*)src);
                var m2 = Avx512F.LoadVector512((int*)src + 32);

                var p = Avx512BW.PackUnsignedSaturate(m1, m2).AsDouble();
                var permuteIndices = Vector512.Create(0L, 2L, 4L, 6L, 1L, 3L, 5L, 7L);

                // DEBUG: _mm512_permutexvar_epi64 is missing, I replicate the functionality with  _mm512_permutex2var_epi64, will check the index later 
                
                // b->chunk0 = Avx512F.PermuteVar8x64x2(p, permuteIndices,p).AsByte();
                b->chunk0 = Avx512F.PermuteVar8x64(p, permuteIndices).AsByte();
            }

            // [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // private static unsafe UInt64 ToBase64Mask(bool base64Url, Block64* b, ref bool error)
            // {
            //     UInt64 m0 = ToBase64Mask(base64Url, ref b->chunk0, ref error);
            //     UInt64 m1 = ToBase64Mask(base64Url, ref b->chunk1, ref error);
            //     return m0 | (m1 << 32);
            // }

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe UInt64 ToBase64Mask(bool base64Url, Block64* b, ref bool error)
            {
                Vector512<byte> input = b->chunk0;
                Vector512<byte> asciiSpaceTbl = Vector512.Create(
                    0, 0, 13, 12, 0, 10, 9, 0, 0, 0, 0, 0, 0, 0, 0, 32,
                    0, 0, 13, 12, 0, 10, 9, 0, 0, 0, 0, 0, 0, 0, 0, 32,
                    0, 0, 13, 12, 0, 10, 9, 0, 0, 0, 0, 0, 0, 0, 0, 32,
                    0, 0, 13, 12, 0, 10, 9, 0, 0, 0, 0, 0, 0, 0, 0, 32).AsByte(); // DEBUG this AsByte is sketch

                Vector512<byte> lookup0 = base64Url ? Vector512.Create(
                        -128, -128, -128, -128, -128, -128, 61, 60, 59, 58, 57, 56, 55, 54, 53, 52, 
                        -128, -128, 62, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -1,
                        -128, -128, -128, -128, -128, -128, -128,-128, -128, -128, -128, -128, -128, -128, -128, -128,
                        -128, -128, -1,-128, -128, -1, -1, -128, -128, -128, -128, -128, -128, -128, -128, -1).AsByte()
                    : Vector512.Create(
                        -128, -128, -128, -128, -128, -128, 61, 60, 59, 58, 57, 56, 55, 54, 53,
                        52, 63, -128, -128, -128, 62, -128, -128, -128, -128, -128, -128, -128,
                        -128, -128, -128, -1, -128, -128, -128, -128, -128, -128, -128, -128,
                        -128, -128, -128, -128, -128, -128, -128, -128, -128, -128, -1, -128,
                        -128, -1, -1, -128, -128, -128, -128, -128, -128, -128, -128, -128).AsByte();

                Vector512<byte> lookup1 = base64Url ? Vector512.Create(
                        -128, -128, -128, -128, -128, 51, 50, 49, 48, 47, 46, 45, 44, 43, 42,
                        41, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 28, 27, 26, -128,
                        63, -128, -128, -128, -128, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15,
                        14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, -128).AsByte()
                    : Vector512.Create(
                        -128, -128, -128, -128, -128, 51, 50, 49, 48, 47, 46, 45, 44, 43, 42,
                        41, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 28, 27, 26, -128,
                        -128, -128, -128, -128, -128, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16,
                        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, -128).AsByte();

                //Vector512<byte> translated = Vector512.Permutex2var(lookup0, input, lookup1);
                Vector512<byte> translated = Avx512Vbmi.PermuteVar64x8x2(lookup0, input, lookup1);
                Vector512<byte> combined = Avx512F.Or(translated.AsInt64(), input.AsInt64()).AsByte();
                // DEBUG: C# does not expose _mm512_movepi8_mask
                UInt64 mask = combined.ExtractMostSignificantBits();

                if (mask != 0)
                {
                    // ascii_space_tbl and input are assumed to be Vector512<byte>
                    Vector512<byte> shuffled = Avx512BW.Shuffle(asciiSpaceTbl, input);

                    // Compare shuffled result with input
                    //DEBUG: this might be wrong : this says  _mm512_cmpeq_epi16 in the documentation but intuitively , doesnt make a lot of sense
                    ulong spaces = Avx512BW.CompareEqual(shuffled, input).ExtractMostSignificantBits();

                    error |= (mask != spaces);
                }

                b->chunk0 = translated;
                return mask;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private unsafe static ulong CompressBlock(ref Block64 b, ulong mask, byte* output, byte* tablePtr)
            {
                // At the time of writing .NET 9.0 does not seem to expose _mm512_maskz_compress_epi8
                // directly, see this discussion:https://github.com/dotnet/runtime/discussions/100829
                ulong nmask = ~mask;
                var part0 = Avx512F.ExtractVector128(b.chunk0.AsByte(), 0);
                var part1 = Avx512F.ExtractVector128(b.chunk0.AsByte(), 1);
                var part2 = Avx512F.ExtractVector128(b.chunk0.AsByte(), 2);
                var part3 = Avx512F.ExtractVector128(b.chunk0.AsByte(), 3);

                Compress(part0, (ushort)mask, output, tablePtr);
                Compress(part1, (ushort)(mask >> 16), output + Popcnt.X64.PopCount(nmask & 0xFFFF), tablePtr);// DEBUG: ushort vs uint32?
                Compress(part2, (ushort)(mask >> 32), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFF), tablePtr);
                Compress(part3, (ushort)(mask >> 48), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFFFFFFUL), tablePtr);

                return Popcnt.X64.PopCount(nmask);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)] // This Compress is the same as in SSE
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
            
            // DEBUG/ optimization: this might be faster
            // public static unsafe void Compress(Vector512<byte> data, uint mask, byte* output)
            // {
            //     if (mask == 0)
            //     {
            //         Avx2.Store(output, data);
            //         return;
            //     }

            //     // Perform compression on the lower 128 bits
            //     Compress(data.GetLower().AsByte(), (ushort)mask, output);

            //     // Perform compression on the upper 128 bits, shifting output pointer by the number of 1's in the lower 16 bits of mask
            //     int popCount = (int)Popcnt.PopCount(~mask & 0xFFFF);
            //     Compress(Avx2.ExtractVector128(data.AsByte(), 1), (ushort)(mask >> 16), output + popCount);
            // }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void CopyBlock(Block64* b, byte* output)
            {
                // Directly store each 128-bit chunk to the output buffer using Avx2
                Avx512F.Store(output, b->chunk0);
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64Decode(byte* output, Vector512<byte> input)
            {
                // Perform multiply and add operations using AVX-512 instructions
                Vector512<short> mergeAbAndBc = Avx512Vbmi.MultiplyAddAdjacent(input, Vector512.Create(0x01400140).AsSByte());//DEBUG: is it really epil16?
                Vector512<int> merged = Avx512BW.MultiplyAddAdjacent(mergeAbAndBc.AsInt16(), Vector512.Create(0x00011000).AsInt16());

                // Define the shuffle pattern
                Vector512<byte> pack = Vector512.Create(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 60, 61, 62, 56, 57, 58,
                52, 53, 54, 48, 49, 50, 44, 45, 46, 40, 41, 42, 36, 37, 38, 32, 33, 34,
                28, 29, 30, 24, 25, 26, 20, 21, 22, 16, 17, 18, 12, 13, 14, 8, 9, 10, 4,
                5, 6, 0, 1, 2).AsByte(); //DEBUG

                // Shuffle based on the predefined pattern
                // Vector512<byte> shuffled = Avx512Vbmi.PermuteVar(pack, merged.AsByte());
                Vector512<byte> shuffled = Avx512Vbmi.Shuffle(pack, merged.AsByte()); //DEBUG: I do not know if this can be shuffled across lanes

                // Store the result back in the output (48 bytes)
                Avx512F.Store(output, shuffled); // Assuming 48 bytes are being written
            }


            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* outPtr, byte* srcPtr)
            {
                Base64Decode(outPtr, Avx512F.LoadVector512(srcPtr));
            }

            // Function to decode a Base64 block into binary data.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe void Base64DecodeBlock(byte* output, Block64* block)
            {
                Base64Decode(output, block->chunk0);
            }


            // Caller is responsible for checking that Avx2.IsSupported && Popcnt.IsSupported
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
                fixed (byte* tablePtr = Tables.pshufbCombineTable)
                {
                    byte* srcEnd = srcInit + source.Length;
                    byte* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    int whiteSpaces = 0;
                    int equalsigns = 0; //DEBUG: not present in C++?

                    int bytesToProcess = source.Length;
                    // skip trailing spaces, DEBUG:not present in the C++?
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
                                    Base64DecodeBlock(dst, &b);
                                    
                                    bufferBytesWritten += 48;
                                    dst += 48;
                                }

                                if (bufferPtr >= (blocksSize - 1) * 64 + startOfBuffer) // We treat the last block separately later on
                                {
                                    for (int i = 0; i < (blocksSize - 1); i++) // We also treat the second to last block differently! Until then it is safe to proceed:
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + i * 64);
                                        bufferBytesWritten += 48;
                                        dst += 48;
                                    }

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
                                SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                            if (result == OperationStatus.InvalidData)
                            {
                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            else
                            {
                                bytesConsumed += remainderBytesConsumed;
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
                fixed (byte* tablePtr = Tables.pshufbCombineTable)
                {
                    byte* srcEnd = srcInit + source.Length;
                    byte* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    int whiteSpaces = 0;
                    int equalsigns = 0; //DEBUG: not present in C++?

                    int bytesToProcess = source.Length;
                    // skip trailing spaces, DEBUG:not present in the C++?
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
                                    Base64DecodeBlock(dst, &b);
                                    
                                    bufferBytesWritten += 48;
                                    dst += 48;
                                }

                                if (bufferPtr >= (blocksSize - 1) * 64 + startOfBuffer) // We treat the last block separately later on
                                {
                                    for (int i = 0; i < (blocksSize - 1); i++) // We also treat the second to last block differently! Until then it is safe to proceed:
                                    {
                                        Base64DecodeBlock(dst, startOfBuffer + i * 64);
                                        bufferBytesWritten += 48;
                                        dst += 48;
                                    }

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
                                SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                            if (result == OperationStatus.InvalidData)
                            {
                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            else
                            {
                                bytesConsumed += remainderBytesConsumed;
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
