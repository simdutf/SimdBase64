using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Collections.Generic;

namespace SimdBase64
{
    namespace Arm {
    public static partial class Base64
    {
        // If needed for debugging, you can do the following:
        /*static string VectorToString(Vector128<byte> vector)
        {
            Span<byte> bytes = new byte[16];
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
            b->chunk0 = AdvSimd.LoadVector128(src);
            b->chunk1 = AdvSimd.LoadVector128(src + 16);
            b->chunk2 = AdvSimd.LoadVector128(src + 32);
            b->chunk3 = AdvSimd.LoadVector128(src + 48);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void LoadBlock(Block64* b, char* src)
        {
            // Load 128 bits (16 chars, 32 bytes) at each step from the UTF-16 source
            var m1 = AdvSimd.LoadVector128((ushort*)src);
            var m2 = AdvSimd.LoadVector128((ushort*)(src + 8));
            var m3 = AdvSimd.LoadVector128((ushort*)(src + 16));
            var m4 = AdvSimd.LoadVector128((ushort*)(src + 24));
            var m5 = AdvSimd.LoadVector128((ushort*)(src + 32));
            var m6 = AdvSimd.LoadVector128((ushort*)(src + 40));
            var m7 = AdvSimd.LoadVector128((ushort*)(src + 48));
            var m8 = AdvSimd.LoadVector128((ushort*)(src + 56));
            // Pack 16-bit chars down to 8-bit chars, handling two vectors at a time
            b->chunk0 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(m1.AsInt16()), m2.AsInt16()).AsByte();
            b->chunk1 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(m3.AsInt16()), m4.AsInt16()).AsByte();
            b->chunk2 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(m5.AsInt16()), m6.AsInt16()).AsByte();
            b->chunk3 = AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(m7.AsInt16()), m8.AsInt16()).AsByte();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong ToBase64MaskRegular(Block64* b, ref bool error)
        {

            // Vector of 0xf for masking lower nibbles
            Vector128<byte> v0f = Vector128.Create((byte)0xf);

            // Extract lower nibbles
            Vector128<byte> loNibbles0 = b->chunk0 & v0f;
            Vector128<byte> loNibbles1 = b->chunk1 & v0f;
            Vector128<byte> loNibbles2 = b->chunk2 & v0f;
            Vector128<byte> loNibbles3 = b->chunk3 & v0f;

            // Extract higher nibbles
            Vector128<byte> hiNibbles0 = AdvSimd.ShiftRightLogical(b->chunk0, 4);
            Vector128<byte> hiNibbles1 = AdvSimd.ShiftRightLogical(b->chunk1, 4);
            Vector128<byte> hiNibbles2 = AdvSimd.ShiftRightLogical(b->chunk2, 4);
            Vector128<byte> hiNibbles3 = AdvSimd.ShiftRightLogical(b->chunk3, 4);

            // Lookup tables for encoding
            Vector128<byte> lutLo = Vector128.Create((byte)0x3A, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x61, 0xE1, 0xB4, 0xE5, 0xE5, 0xF4, 0xB4);

            Vector128<byte> lutHi = Vector128.Create((byte)0x11, 0x20, 0x42, 0x80, 0x8, 0x4, 0x8, 0x4,
                                        0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20);
            // Lookup for lower and higher nibbles
            Vector128<byte> lo0 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles0);
            Vector128<byte> hi0 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles0);
            Vector128<byte> lo1 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles1);
            Vector128<byte> hi1 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles1);
            Vector128<byte> lo2 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles2);
            Vector128<byte> hi2 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles2);
            Vector128<byte> lo3 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles3);
            Vector128<byte> hi3 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles3);
            // Check for invalid characters
            // Note that the maxaccross can be replaced.
            byte check = AdvSimd.Arm64.MaxAcross((hi0 & lo0) |  (lo1 & hi1) | (lo2 & hi2) | (lo3 & hi3)).ToScalar();

            error = (check > 0x3);

            ulong badCharmask = 0;
            if (check != 0)
            {
                Vector128<byte> test0 = AdvSimd.CompareTest(lo0, hi0);
                Vector128<byte> test1 = AdvSimd.CompareTest(lo1, hi1);
                Vector128<byte> test2 = AdvSimd.CompareTest(lo2, hi2);
                Vector128<byte> test3 = AdvSimd.CompareTest(lo3, hi3);
                Vector128<byte> bit_mask = Vector128.Create((byte)0x01, 0x02, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80,
                              0x01, 0x02, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80);
                Vector128<byte> sum0 = AdvSimd.Arm64.AddPairwise(test0 & bit_mask, test1 & bit_mask);
                Vector128<byte> sum1 = AdvSimd.Arm64.AddPairwise(test2 & bit_mask, test3 & bit_mask);
                sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum1);
                sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum0);
                badCharmask = sum0.AsUInt64().ToScalar();
            }

            Vector128<byte> roll_lut = Vector128.Create((byte)0x0, 0x10, 0x13, 0x4, 0xbf, 0xbf, 0xb9, 0xb9,
                                0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0);
            Vector128<byte> SecondLast = Vector128.Create((byte)0x2f);
            Vector128<byte>  roll0 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk0, SecondLast) + hiNibbles0);
            Vector128<byte>  roll1 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk1, SecondLast) + hiNibbles1);
            Vector128<byte>  roll2 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk2, SecondLast) + hiNibbles2);
            Vector128<byte>  roll3 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk3, SecondLast) + hiNibbles3);
            b->chunk0 += roll0;
            b->chunk1 += roll1;
            b->chunk2 += roll2;
            b->chunk3 += roll3;
            return badCharmask;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe ulong ToBase64MaskUrl(Block64* b, ref bool error)
        {
            // Vector of 0xf for masking lower nibbles
            Vector128<byte> v0f = Vector128.Create((byte)0xf);

            Vector128<byte> underscore0 = Vector128.Equals(b->chunk0, Vector128.Create((byte)0x5f));
            Vector128<byte> underscore1 = Vector128.Equals(b->chunk1, Vector128.Create((byte)0x5f));
            Vector128<byte> underscore2 = Vector128.Equals(b->chunk2, Vector128.Create((byte)0x5f));
            Vector128<byte> underscore3 = Vector128.Equals(b->chunk3, Vector128.Create((byte)0x5f));

            // Extract lower nibbles
            Vector128<byte> loNibbles0 = b->chunk0 & v0f;
            Vector128<byte> loNibbles1 = b->chunk1 & v0f;
            Vector128<byte> loNibbles2 = b->chunk2 & v0f;
            Vector128<byte> loNibbles3 = b->chunk3 & v0f;

            // Extract higher nibbles
            Vector128<byte> hiNibbles0 = AdvSimd.ShiftRightLogical(b->chunk0, 4);
            Vector128<byte> hiNibbles1 = AdvSimd.ShiftRightLogical(b->chunk1, 4);
            Vector128<byte> hiNibbles2 = AdvSimd.ShiftRightLogical(b->chunk2, 4);
            Vector128<byte> hiNibbles3 = AdvSimd.ShiftRightLogical(b->chunk3, 4);

            // Lookup tables for encoding
            Vector128<byte> lutLo = Vector128.Create((byte)0x3A, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x70, 0x61, 0xE1, 0xF4, 0xE5, 0xA5, 0xF4, 0xF4);

            Vector128<byte> lutHi = Vector128.Create((byte)0x11, 0x20, 0x42, 0x80, 0x8, 0x4, 0x8, 0x4,
                                        0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20);
            // Lookup for lower and higher nibbles
            Vector128<byte> lo0 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles0);
            Vector128<byte> hi0 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles0);
            Vector128<byte> lo1 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles1);
            Vector128<byte> hi1 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles1);
            Vector128<byte> lo2 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles2);
            Vector128<byte> hi2 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles2);
            Vector128<byte> lo3 = AdvSimd.Arm64.VectorTableLookup(lutLo, loNibbles3);
            Vector128<byte> hi3 = AdvSimd.Arm64.VectorTableLookup(lutHi, hiNibbles3);

            hi0 = AdvSimd.BitwiseClear(hi0, underscore0);
            hi1 = AdvSimd.BitwiseClear(hi1, underscore1);
            hi2 = AdvSimd.BitwiseClear(hi2, underscore2);
            hi3 = AdvSimd.BitwiseClear(hi3, underscore3);

            // Check for invalid characters
            // Note that the maxaccross can be replaced.
            byte check = AdvSimd.Arm64.MaxAcross((hi0 & lo0) |  (lo1 & hi1) | (lo2 & hi2) | (lo3 & hi3)).ToScalar();

            error = (check > 0x3);

            ulong badCharmask = 0;
            if (check != 0)
            {
                Vector128<byte> test0 = AdvSimd.CompareTest(lo0, hi0);
                Vector128<byte> test1 = AdvSimd.CompareTest(lo1, hi1);
                Vector128<byte> test2 = AdvSimd.CompareTest(lo2, hi2);
                Vector128<byte> test3 = AdvSimd.CompareTest(lo3, hi3);
                Vector128<byte> bit_mask = Vector128.Create((byte)0x01, 0x02, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80,
                              0x01, 0x02, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80);
                Vector128<byte> sum0 = AdvSimd.Arm64.AddPairwise(test0 & bit_mask, test1 & bit_mask);
                Vector128<byte> sum1 = AdvSimd.Arm64.AddPairwise(test2 & bit_mask, test3 & bit_mask);
                sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum1);
                sum0 = AdvSimd.Arm64.AddPairwise(sum0, sum0);
                badCharmask = sum0.AsUInt64().ToScalar();
            }

            Vector128<byte> roll_lut = Vector128.Create((byte)0xe0, 0x11, 0x13, 0x4, 0xbf, 0xbf, 0xb9, 0xb9,
                                0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0);
            Vector128<byte> SecondLast = Vector128.Create((byte)0x2d);
            hiNibbles0 = AdvSimd.BitwiseClear(hiNibbles0, underscore0);
            hiNibbles1 = AdvSimd.BitwiseClear(hiNibbles1, underscore1);
            hiNibbles2 = AdvSimd.BitwiseClear(hiNibbles2, underscore2);
            hiNibbles3 = AdvSimd.BitwiseClear(hiNibbles3, underscore3);
            Vector128<byte>  roll0 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk0, SecondLast) + hiNibbles0);
            Vector128<byte>  roll1 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk1, SecondLast) + hiNibbles1);
            Vector128<byte>  roll2 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk2, SecondLast) + hiNibbles2);
            Vector128<byte>  roll3 = AdvSimd.Arm64.VectorTableLookup(roll_lut, AdvSimd.CompareEqual(b->chunk3, SecondLast) + hiNibbles3);
            b->chunk0 += roll0;
            b->chunk1 += roll1;
            b->chunk2 += roll2;
            b->chunk3 += roll3;
            return badCharmask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static ulong CompressBlock(ref Block64 b, ulong mask, byte* output)
        {
            ulong nmask = ~mask;
            Compress(b.chunk0, (ushort)mask, output);
            Compress(b.chunk1, (ushort)(mask >> 16), output + UInt64.PopCount(nmask & 0xFFFF));
            Compress(b.chunk2, (ushort)(mask >> 32), output + UInt64.PopCount(nmask & 0xFFFFFFFF));
            Compress(b.chunk3, (ushort)(mask >> 48), output + UInt64.PopCount(nmask & 0xFFFFFFFFFFFFUL));

            return UInt64.PopCount(nmask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Compress(Vector128<byte> data, ushort mask, byte* output)
        {
            if (mask == 0)
            {
                Vector128.Store(data, output);
                return;
            }

            // this particular implementation was inspired by work done by @animetosho
            // we do it in two steps, first 8 bytes and then second 8 bytes
            byte mask1 = (byte)mask;      // least significant 8 bits
            byte mask2 = (byte)(mask >> 8); // most significant 8 bits
            // next line just loads the 64-bit values thintable_epi8[mask1] and
            // thintable_epi8[mask2] into a 128-bit register, using only
            // two instructions on most compilers.

            ulong value1 = Tables.thintableEpi8[mask1];
            ulong value2 = Tables.thintableEpi8[mask2];

            Vector128<sbyte> shufmask = Vector128.Create(value2, value1).AsSByte();

            // Increment by 0x08 the second half of the mask
            Vector128<sbyte> increment = Vector128.Create(0x08080808, 0x08080808, 0, 0).AsSByte();
            shufmask = shufmask + increment;

            // this is the version "nearly pruned"
            Vector128<sbyte> pruned = AdvSimd.Arm64.VectorTableLookup(data.AsSByte(), shufmask);
            // we still need to put the two halves together.
            // we compute the popcount of the first half:
            int pop1 = Tables.BitsSetTable256mul2[mask1];
            // then load the corresponding mask, what it does is to write
            // only the first pop1 bytes from the first 8 bytes, and then
            // it fills in with the bytes from the second 8 bytes + some filling
            // at the end.

            fixed (byte* tablePtr = Tables.pshufbCombineTable)
            {
                Vector128<byte> compactmask = Vector128.Load(tablePtr + pop1 * 8);

                Vector128<byte> answer = AdvSimd.Arm64.VectorTableLookup(pruned.AsByte(), compactmask);
                Vector128.Store(answer, output);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CopyBlock(Block64* b, byte* output)
        {
            Vector128.Store(b->chunk0, output);
            Vector128.Store(b->chunk1, output + 16);
            Vector128.Store(b->chunk2, output + 32);
            Vector128.Store(b->chunk3, output + 48);
        }
  

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void Base64DecodeBlock(byte* outPtr, byte* srcPtr)
        {
            // Load 4 vectors from src
            var (str0, str1, str2, str3) = AdvSimd.Arm64.Load4xVector128AndUnzip(srcPtr);



            // Perform bitwise operations to simulate NEON intrinsics
            Vector128<byte> outvec0 = AdvSimd.Or(
                AdvSimd.ShiftLeftLogical(str0, 2),
                AdvSimd.ShiftRightLogical(str1, 4)
            );

            Vector128<byte> outvec1 = AdvSimd.Or(
                AdvSimd.ShiftLeftLogical(str1, 4),
                AdvSimd.ShiftRightLogical(str2, 2)
            );

            Vector128<byte> outvec2 = AdvSimd.Or(
                AdvSimd.ShiftLeftLogical(str2, 6),
                str3
            );

            // Store the result in outData
            AdvSimd.Arm64.StoreVectorAndZip(outPtr, (outvec0, outvec1, outvec2));
        }

        // Caller is responsible for checking that (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
        public unsafe static OperationStatus DecodeFromBase64ARM(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
        {
            if (isUrl)
            {
                return InnerDecodeFromBase64ARMUrl(source, dest, out bytesConsumed, out bytesWritten);
            }
            else
            {
                return InnerDecodeFromBase64ARMRegular(source, dest, out bytesConsumed, out bytesWritten);
            }
        }

        public unsafe static OperationStatus DecodeFromBase64ARM(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
        {
            if (isUrl)
            {
                return InnerDecodeFromBase64ARMUrl(source, dest, out bytesConsumed, out bytesWritten);
            }
            else
            {
                return InnerDecodeFromBase64ARMRegular(source, dest, out bytesConsumed, out bytesWritten);
            }
        }

        private unsafe static OperationStatus InnerDecodeFromBase64ARMRegular(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
        {
            // translation from ASCII to 6 bit values
            bool isUrl = false;
            byte[] toBase64 = Tables.ToBase64Value;
            bytesConsumed = 0;
            bytesWritten = 0;
            const int blocksSize = 6;
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
                while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                {
                    bytesToProcess--;
                    whiteSpaces++;
                }

                int equallocation = bytesToProcess; // location of the first padding character if any
                if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                {
                    bytesToProcess -= 1;
                    equalsigns++;
                    while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
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
                            UInt64 badCharMask = Base64.ToBase64MaskRegular(&b, ref error);

                            if (error == true)
                            {
                                src -= bufferBytesConsumed;
                                dst -= bufferBytesWritten;
                                bytesConsumed = Math.Max(0, (int)(src - srcInit));
                                bytesWritten = Math.Max(0, (int)(dst - dstInit));

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                            else
                            {
                                CopyBlock(&b, bufferPtr);
                                bufferPtr += 64;
                                bufferBytesConsumed += 64;
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
                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                                byte val = toBase64[(byte)*src];
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
                            SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);


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

        private unsafe static OperationStatus InnerDecodeFromBase64ARMRegular(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
        {
            // translation from ASCII to 6 bit values
            bool isUrl = false;
            byte[] toBase64 = Tables.ToBase64Value;
            bytesConsumed = 0;
            bytesWritten = 0;
            const int blocksSize = 6;
            Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            // Define pointers within the fixed blocks
            fixed (char* srcInit = source)
            fixed (byte* dstInit = dest)
            fixed (byte* startOfBuffer = buffer)
            {
                char* srcEnd = srcInit + source.Length;
                char* src = srcInit;
                byte* dst = dstInit;
                byte* dstEnd = dstInit + dest.Length;

                int whiteSpaces = 0;
                int equalsigns = 0;

                int bytesToProcess = source.Length;
                // skip trailing spaces
                while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                {
                    bytesToProcess--;
                    whiteSpaces++;
                }

                int equallocation = bytesToProcess; // location of the first padding character if any
                if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                {
                    bytesToProcess -= 1;
                    equalsigns++;
                    while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
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
                        char* srcEnd64 = srcInit + bytesToProcess - 64;
                        while (src <= srcEnd64)
                        {
                            Base64.Block64 b;
                            Base64.LoadBlock(&b, src);
                            src += 64;
                            bufferBytesConsumed += 64;
                            bool error = false;
                            UInt64 badCharMask = Base64.ToBase64MaskRegular(&b, ref error);
                            if (error == true)
                            {
                                src -= bufferBytesConsumed;
                                dst -= bufferBytesWritten;

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);
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
                            else
                            {
                                CopyBlock(&b, bufferPtr);
                                bufferPtr += 64;
                                bufferBytesConsumed += 64;
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

                            if (!SimdBase64.Base64.IsValidBase64Index(*src))
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                                if (!SimdBase64.Base64.IsValidBase64Index(*src))
                                {
                                    bytesConsumed = (int)(src - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.InvalidData;
                                }

                                byte val = toBase64[(byte)*src];
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
                            SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);


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
        private unsafe static OperationStatus InnerDecodeFromBase64ARMUrl(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
        {
            // translation from ASCII to 6 bit values
            bool isUrl = true;
            byte[] toBase64 = Tables.ToBase64UrlValue;
            bytesConsumed = 0;
            bytesWritten = 0;
            const int blocksSize = 6;
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
                while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                {
                    bytesToProcess--;
                    whiteSpaces++;
                }

                int equallocation = bytesToProcess; // location of the first padding character if any
                if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                {
                    bytesToProcess -= 1;
                    equalsigns++;
                    while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
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
                            UInt64 badCharMask = Base64.ToBase64MaskUrl(&b, ref error);
                            if (error == true)
                            {
                                src -= bufferBytesConsumed;
                                dst -= bufferBytesWritten;

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                            else
                            {
                                CopyBlock(&b, bufferPtr);
                                bufferPtr += 64;
                                bufferBytesConsumed += 64;
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
                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                                byte val = toBase64[(byte)*src];
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
                            SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);


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

        private unsafe static OperationStatus InnerDecodeFromBase64ARMUrl(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
        {
            // translation from ASCII to 6 bit values
            bool isUrl = true;
            byte[] toBase64 = Tables.ToBase64UrlValue;
            bytesConsumed = 0;
            bytesWritten = 0;
            const int blocksSize = 6;
            Span<byte> buffer = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            // Define pointers within the fixed blocks
            fixed (char* srcInit = source)
            fixed (byte* dstInit = dest)
            fixed (byte* startOfBuffer = buffer)
            {
                char* srcEnd = srcInit + source.Length;
                char* src = srcInit;
                byte* dst = dstInit;
                byte* dstEnd = dstInit + dest.Length;

                int whiteSpaces = 0;
                int equalsigns = 0;

                int bytesToProcess = source.Length;
                // skip trailing spaces
                while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                {
                    bytesToProcess--;
                    whiteSpaces++;
                }

                int equallocation = bytesToProcess; // location of the first padding character if any
                if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                {
                    bytesToProcess -= 1;
                    equalsigns++;
                    while (bytesToProcess > 0 && SimdBase64.Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
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
                        char* srcEnd64 = srcInit + bytesToProcess - 64;
                        while (src <= srcEnd64)
                        {
                            Base64.Block64 b;
                            Base64.LoadBlock(&b, src);
                            src += 64;
                            bufferBytesConsumed += 64;
                            bool error = false;
                            UInt64 badCharMask = Base64.ToBase64MaskUrl(&b, ref error);
                            if (error == true)
                            {
                                src -= bufferBytesConsumed;
                                dst -= bufferBytesWritten;

                                bytesConsumed = Math.Max(0, (int)(src - srcInit));
                                bytesWritten = Math.Max(0, (int)(dst - dstInit));

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                            else
                            {
                                CopyBlock(&b, bufferPtr);
                                bufferPtr += 64;
                                bufferBytesConsumed += 64;
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

                            if (!SimdBase64.Base64.IsValidBase64Index(*src))
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

                                bytesConsumed += remainderBytesConsumed;
                                bytesWritten += remainderBytesWritten;
                                return result;
                            }
                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = Math.Max(0, (int)(src - srcInit) - lastBlockSrcCount - (int)bufferBytesConsumed);
                                bytesWritten = Math.Max(0, (int)(dst - dstInit) - (int)bufferBytesWritten);

                                int remainderBytesConsumed = 0;
                                int remainderBytesWritten = 0;

                                OperationStatus result =
                                    SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(Math.Max(0, bytesConsumed)), dest.Slice(Math.Max(0, bytesWritten)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);

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
                                if (!SimdBase64.Base64.IsValidBase64Index(*src))
                                {
                                    bytesConsumed = (int)(src - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.InvalidData;
                                }
                                byte val = toBase64[(byte)*src];
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
                            SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);


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
