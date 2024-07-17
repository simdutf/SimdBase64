using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Numerics;

namespace SimdBase64
{
    public static partial class Base64
    {

        [StructLayout(LayoutKind.Sequential)]
        public struct Block64
        {
            public Vector128<byte> chunk0;
            public Vector128<byte> chunk1;
            public Vector128<byte> chunk2;
            public Vector128<byte> chunk3;

            public override bool Equals(object obj)
            {
                throw new NotImplementedException();
            }

            public override int GetHashCode()
            {
                throw new NotImplementedException();
            }

            public static bool operator ==(Block64 left, Block64 right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Block64 left, Block64 right)
            {
                return !(left == right);
            }
        }

        // Load 64 bytes into a block64 structure
        public static unsafe void LoadBlock(Block64* b, byte* src)
        {
            b->chunk0 = Sse2.LoadVector128(src);
            b->chunk1 = Sse2.LoadVector128(src + 16);
            b->chunk2 = Sse2.LoadVector128(src + 32);
            b->chunk3 = Sse2.LoadVector128(src + 48);
        }

        public static unsafe ulong ToBase64Mask(bool base64Url, Block64* b, out bool error)
        {
            error = false;
            ulong m0 = ToBase64Mask(base64Url, ref b->chunk0, out error);
            ulong m1 = ToBase64Mask(base64Url, ref b->chunk1, out error);
            ulong m2 = ToBase64Mask(base64Url, ref b->chunk2, out error);
            ulong m3 = ToBase64Mask(base64Url, ref b->chunk3, out error);
            return m0 | (m1 << 16) | (m2 << 32) | (m3 << 48);
        }

        public static ushort ToBase64Mask(bool base64Url, ref Vector128<byte> src, out bool error)
        {
            error = false;
            Vector128<sbyte> asciiSpaceTbl = Vector128.Create(
                0x20, 0x0, 0x0, 0x0,
                0x0, 0x0, 0x0, 0x0,
                0x0, 0x9, 0xa, 0x0,
                0xc, 0xd, 0x0, 0x0
            );

            // credit: aqrit
            Vector128<sbyte> deltaAsso;
            if (base64Url)
            {
                deltaAsso = Vector128.Create(   0x1, 0x1, 0x1, 0x1,
                                                0x1, 0x1, 0x1, 0x1,
                                                0x0, 0x0, 0x0, 0x0,
                                                0x0, 0xF, 0x0, 0xF);
            }
            else
            {
                deltaAsso = Vector128.Create(
                   0x1, 0x1, 0x1, 0x1,
                   0x1, 0x1, 0x1, 0x1,
                   0x0, 0x0, 0x0, 0x0,
                   0x0, 0xF, 0x0, 0xF
               );
            }

            Vector128<sbyte> deltaValues;
            if (base64Url)
            {
                deltaValues = Vector128.Create(
                    // 0x00, 0x00, 0x00, 0x13, // DEBUG: Potentially an error? the first row is not explicitly cast in the C++
                    // 0x04, (byte)0xBF, (byte)0xBF, (byte)0xB9,
                    // (byte)0xB9, 0x00, 0x11, (byte)0xC3,
                    // (byte)0xBF, (byte)0xE0, (byte)0xB9, (byte)0xB9

                    0x00, 0x00, 0x00, 0x13, // DEBUG: Potentially an error? the first row is not explicitly cast in the C++
                    0x04, 0xBF,0xBF, 0xB9,
                    0xB9, 0x00, 0x11, 0xC3,
                    0xBF, 0xE0, 0xB9, 0xB9
                ).AsSByte();
            }
            else
            {
                deltaValues = Vector128.Create(
                    (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x13,
                    (byte)0x04, (byte)0xBF, (byte)0xBF, (byte)0xB9,
                    (byte)0xB9, (byte)0x00, (byte)0x10, (byte)0xC3,
                    (byte)0xBF, (byte)0xBF, (byte)0xB9, (byte)0xB9
                ).AsSByte();
            }
            Vector128<sbyte> checkAsso;
            Vector128<sbyte> checkValues;

            if (base64Url)
            {
                checkAsso = Vector128.Create(
                    0x0D, 0x01, 0x01, 0x01,
                    0x01, 0x01, 0x01, 0x01,
                    0x01, 0x01, 0x03, 0x07,
                    0x0B, 0x06, 0x0B, 0x12
                );

                checkValues = Vector128.Create(
                    0x00, 0x80, 0x80, 0x80,// explcitly cast as uint8_t in the C++
                    0xCF, 0xBF, 0xD3, 0xA6,
                    0xB5, 0x86, 0xD0, 0x80,
                    0xB0, 0x80, 0x00, 0x00
                ).AsSByte();
            }
            else
            {
                checkAsso = Vector128.Create(
                    0x0D, 0x01, 0x01, 0x01,
                    0x01, 0x01, 0x01, 0x01,
                    0x01, 0x01, 0x03, 0x07,
                    0x0B, 0x0B, 0x0B, 0x0F
                );

                checkValues = Vector128.Create(
                    (byte)0x80, (byte)0x80, (byte)0x80, (byte)0x80,
                    (byte)0xCF, (byte)0xBF, (byte)0xD5, (byte)0xA6,
                    (byte)0xB5, (byte)0x86, (byte)0xD1, (byte)0x80,
                    (byte)0xB1, (byte)0x80, (byte)0x91, (byte)0x80
                ).AsSByte();
            }

            Vector128<sbyte> shifted = Sse2.ShiftRightLogical(src.AsInt32(), 3).AsSByte();

            Vector128<byte> deltaHash = Sse2.Average(Ssse3.Shuffle(deltaAsso, src.AsSByte()).AsByte(), shifted.AsByte());
            Vector128<byte> checkHash = Sse2.Average(Ssse3.Shuffle(checkAsso, src.AsSByte()).AsByte(), shifted.AsByte());
// You were here
            Vector128<sbyte> outVector = Sse2.AddSaturate(Ssse3.Shuffle(deltaValues, deltaHash.AsSByte()), src.AsSByte());
            Vector128<sbyte> chkVector = Sse2.AddSaturate(Ssse3.Shuffle(checkValues, checkHash.AsSByte()), src.AsSByte());

            int mask = Sse2.MoveMask(chkVector);
            if (mask != 0)
            {
                // indicates which bytes of the src is ASCII and which isnt 
                Vector128<sbyte> asciiSpace = Sse2.CompareEqual(Ssse3.Shuffle(asciiSpaceTbl.AsByte(), src).AsSByte(), src.AsSByte());
                // Movemask extract the MSB from each byte of asciispace
                // if the mask is not the same as the movemask extract, signal an error
                        // Print the vectors and mask

                error |= mask != Sse2.MoveMask(asciiSpace);
            }

            

            src = outVector.AsByte();
            return (ushort)mask;
        }

        public unsafe static ulong CompressBlock(ref Block64 b, ulong mask, byte* output)
        {
            ulong nmask = ~mask;

            Compress(b.chunk0, (ushort)mask, output);
            Compress(b.chunk1, (ushort)(mask >> 16), output + Popcnt.X64.PopCount(nmask & 0xFFFF));
            Compress(b.chunk2, (ushort)(mask >> 32), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFF));
            Compress(b.chunk3, (ushort)(mask >> 48), output + Popcnt.X64.PopCount(nmask & 0xFFFFFFFFFFFFUL));

            return Popcnt.X64.PopCount(nmask);
        }

        public static unsafe void Compress(Vector128<byte> data, ushort mask, byte* output)
        {
            if (mask == 0)
            {
                Sse2.Store(output, data);
            }
        }

        public static unsafe void CopyBlock(Block64* b, byte* output)
        {
            // Directly store each 128-bit chunk to the output buffer using SSE2
            Sse2.Store(output, b->chunk0);
            Sse2.Store(output + 16, b->chunk1);
            Sse2.Store(output + 32, b->chunk2);
            Sse2.Store(output + 48, b->chunk3);
        }

        public static unsafe void Base64DecodeBlockSafe(byte* outPtr, Block64* b)
        {
            Base64Decode(outPtr, b->chunk0);
            Base64Decode(outPtr + 12, b->chunk1);
            Base64Decode(outPtr + 24, b->chunk2);
        }

        public unsafe static void Base64Decode(byte* output, Vector128<byte> input)
        {
            Vector128<byte> packShuffle = Vector128.Create((byte)2, (byte)1, (byte)0, (byte)6, (byte)5, (byte)4,
                                                         (byte)10, (byte)9, (byte)8, (byte)14, (byte)13, (byte)12,
                                                         (byte)255, (byte)255, (byte)255, (byte)255);//255 corresponds to -1

            // Perform the initial multiply and add operation across unsigned 8-bit integers.
            // DEBUG:This looks sus?
            Vector128<int> t0 = Sse3.MultiplyAddAdjacent(input.AsInt16(), Vector128.Create(0x01400140).AsInt16());

            // Perform another multiply and add to finalize the byte positions.
            Vector128<int> t1 = Sse2.MultiplyAddAdjacent(t0.AsInt16(), Vector128.Create(0x00011000).AsInt16());

            // Shuffle the bytes according to the packShuffle pattern.
            Vector128<byte> t2 = Ssse3.Shuffle(t1.AsByte(), packShuffle);

            // Store the output. This writes 16 bytes, but we only need 12.
            Sse2.Store(output, t2);
        }

        public static unsafe void Base64DecodeBlock(byte* outPtr, byte* srcPtr)
        {
            Base64Decode(outPtr, Sse2.LoadVector128(srcPtr));
            Base64Decode(outPtr + 12, Sse2.LoadVector128(srcPtr + 16));
            Base64Decode(outPtr + 24, Sse2.LoadVector128(srcPtr + 32));
            Base64Decode(outPtr + 36, Sse2.LoadVector128(srcPtr + 48));
        }

        // Function to decode a Base64 block into binary data.
        public static unsafe void Base64DecodeBlock(byte* output, Block64* block)
        {
            Base64Decode(output, block->chunk0);
            Base64Decode(output + 12, block->chunk1);
            Base64Decode(output + 24, block->chunk2);
            Base64Decode(output + 36, block->chunk3);
        }

        public static unsafe void Base64DecodeBlockSafe(byte* outPtr, byte* srcPtr)
        {
            Base64Decode(outPtr, Sse2.LoadVector128(srcPtr));
            Base64Decode(outPtr + 12, Sse2.LoadVector128(srcPtr + 16));
            Base64Decode(outPtr + 24, Sse2.LoadVector128(srcPtr + 32));
            Vector128<byte> tempBlock = Sse2.LoadVector128(srcPtr + 48);
            byte[] buffer = new byte[16];
            fixed (byte* bufferPtr = buffer)
            {
                Base64Decode(bufferPtr, tempBlock);

                // Copy only the first 12 bytes of the decoded fourth block into the output buffer, offset by 36 bytes.
                // This step is necessary because the fourth block may not need all 16 bytes if it contains padding characters.
                Buffer.MemoryCopy(bufferPtr, outPtr + 36, 12, 12);
            }
        }

        public unsafe static OperationStatus SafeDecodeFromBase64SSE(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
        {
            // translation from ASCII to 6 bit values
            byte[] toBase64 = isUrl == true ? Tables.ToBase64UrlValue : Tables.ToBase64Value;

            bytesConsumed = 0;
            bytesWritten = 0;


            // Define pointers within the fixed blocks
            fixed (byte* srcInit = source)
            fixed (byte* dstInit = dest)

            {
                byte* srcEnd = srcInit + source.Length;
                byte* src = srcInit;
                byte* dst = dstInit;
                byte* dstEnd = dstInit + dest.Length;

                int bytesToProcess = source.Length;
                int whiteSpaces = 0;
                // skip trailing spaces
                while (bytesToProcess > 0 && Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
                {
                    bytesToProcess--;
                    whiteSpaces++;
                }

                int equallocation = bytesToProcess; // location of the first padding character if any
                int equalsigns = 0;
                if (bytesToProcess > 0 && source[bytesToProcess - 1] == '=')
                {
                    bytesToProcess -= 1;
                    equalsigns++;
                    while (bytesToProcess > 0 && Base64.IsAsciiWhiteSpace((char)source[bytesToProcess - 1]))
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

                // Console.WriteLine("Skipped white spaces:", equallocation,); //DEBUG

                byte* endOfSafe64ByteZone =
                    // round up to the nearest multiple of 4, then multiplied by 3
                    (bytesToProcess + 3) / 4 * 3 >= 63 ?
                            dst + (bytesToProcess + 3) / 4 * 3 - 63 :
                            dst;

                const int blockSize = 6;
                Debug.Equals(blockSize >= 2, "block should of size 2 or more");
                byte[] buffer = new byte[blockSize * 64];
                fixed (byte* startOfBuffer = buffer)
                {
                    byte* bufferPtr = startOfBuffer;
                    if (bytesToProcess >= 64)
                    {
                        byte* srcend64 = srcInit + bytesToProcess - 64;
                        while (src <= srcend64)
                        {
                            Base64.Block64 b; 
                            Base64.LoadBlock(&b, src);
                            src += 64;
                            bool error = false;
                            UInt64 badCharMask = Base64.ToBase64Mask(isUrl, &b, out error); 
                            // badchar indicate an error but at a later date: it is fed into the CompressBlock function later on.
                            // error indicates that a particular error: TODO 
                            if (error)
                            {
                                Console.WriteLine("Error found in 4x Vector128 block!");
                                src -= 64;
                                while (src < srcInit + bytesToProcess && toBase64[Convert.ToByte(*src)] <= 64)
                                {
                                    src++;
                                }
                                bytesConsumed = (int)(src - srcInit); 
                                bytesWritten = (int)(dst - dstInit);// TODO: this and its other brethen when an error occurs is likely wrong
                                return OperationStatus.InvalidData;
                            }
                            if (badCharMask != 0)
                            {
                                // optimization opportunity: check for simple masks like those made of
                                // continuous 1s followed by continuous 0s. And masks containing a
                                // single bad character.
                                bufferPtr += CompressBlock(ref b, badCharMask, bufferPtr);
                                // Compressblock only stores 
                            }
                            else if (bufferPtr != startOfBuffer)
                            {
                                CopyBlock(&b, bufferPtr);
                                bufferPtr += 64;
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
                                dst += 48;
                            }
                            if (bufferPtr >= (blockSize - 1) * 64 + startOfBuffer)
                            {
                                for (int i = 0; i < (blockSize - 2); i++)
                                {
                                    Base64DecodeBlock(dst, startOfBuffer + i * 64);
                                    dst += 48;
                                }
                                if (dst >= endOfSafe64ByteZone)
                                {
                                    Base64DecodeBlockSafe(dst, startOfBuffer + (blockSize - 2) * 64);
                                }
                                else
                                {
                                    Base64DecodeBlock(dst, startOfBuffer + (blockSize - 2) * 64);
                                }
                                dst += 48;
                                Buffer.MemoryCopy(bufferPtr + (blockSize - 1) * 64, bufferPtr, 64, 64);
                                bufferPtr -= (blockSize - 1) * 64;
                            }
                        }
                    }

                    // Optimization note: if this is almost full, then it is worth our
                    // time, otherwise, we should just decode directly.
                    int lastBlock = (int)((bufferPtr - startOfBuffer) % 64);
                    if (lastBlock != 0 && srcEnd - src + lastBlock >= 64)
                    {
                        while ((bufferPtr - startOfBuffer) % 64 != 0 && src < srcEnd)
                        {
                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit); 
                                return OperationStatus.InvalidData;
                            }
                            bufferPtr += (val <= 63) ? 1 : 0;
                            src++;
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
                        dst += 48;
                    }
                    if ((bufferPtr - subBufferPtr) % 64 != 0)
                    {
                        while (subBufferPtr + 4 < bufferPtr)
                        {
                            UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                << 8;
                            triple = SwapBytes(triple);
                            Buffer.MemoryCopy(&triple, dst, 4, 4);

                            dst += 3;
                            subBufferPtr += 4;
                        }
                        if (subBufferPtr + 4 <= bufferPtr)
                        {
                            UInt32 triple = (((UInt32)((byte)(subBufferPtr[0])) << 3 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[1])) << 2 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[2])) << 1 * 6) +
                                                ((UInt32)((byte)(subBufferPtr[3])) << 0 * 6))
                                                << 8;
                            triple = SwapBytes(triple);
                            Buffer.MemoryCopy(&triple, dst, 3, 3);

                            dst += 3;
                            subBufferPtr += 4;
                        }
                        // we may have 1, 2 or 3 bytes left and we need to decode them so let us
                        // bring in src content
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
                                bytesWritten = (int)(dst - dstInit); // TODO
                                return OperationStatus.NeedMoreData;
                            }
                            if (leftover == 2)
                            {
                                UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                ((UInt32)(subBufferPtr[1]) << 2 * 6);
                                triple = SwapBytes(triple);
                                triple >>= 8;
                                Buffer.MemoryCopy(&triple, dst, 1, 1);
                                dst += 1;
                            }
                            else if (leftover == 3)
                            {
                                UInt32 triple = ((UInt32)(subBufferPtr[0]) << 3 * 6) +
                                                ((UInt32)(subBufferPtr[1]) << 2 * 6) +
                                                ((UInt32)(subBufferPtr[2]) << 1 * 6);
                                triple = SwapBytes(triple);

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
                                triple = SwapBytes(triple);
                                Buffer.MemoryCopy(&triple, dst, 3, 3);

                                dst += 3;
                            }
                        }
                    }
                    if (src < srcEnd + equalsigns)
                    {
                        int remainderBytesConsumed = 0;
                        int remainderBytesWritten = 0;

                        OperationStatus result =
                            Base64WithWhiteSpaceToBinaryScalar(source.Slice((int)(src - srcInit)), dest.Slice((int)(dst - dstInit)), out remainderBytesConsumed, out remainderBytesWritten, isUrl);
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
                    if (equalsigns > 0)
                    {
                        if (((int)(dst - dstInit) % 3 == 0) || (((int)(dst - dstInit) % 3) + 1 + equalsigns != 4))
                        {
                            return OperationStatus.InvalidData;
                        }
                    }
                    return OperationStatus.Done;
                }

            }
        }



    }
}
