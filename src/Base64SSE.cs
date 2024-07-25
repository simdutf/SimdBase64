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
        public struct Block64 : IEquatable<Block64>
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

            public bool Equals(Block64 other)
            {
                throw new NotImplementedException();
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
                deltaAsso = Vector128.Create(0x1, 0x1, 0x1, 0x1,
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
                    0x00, 0x00, 0x00, 0x13, 
                    0x04, 0xBF, 0xBF, 0xB9,
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

            int mask = Sse2.MoveMask(chkVector.AsDouble());
            if (mask != 0)
            {

                // indicates which bytes of the src is ASCII and which isnt 
                Vector128<byte> asciiSpace = Sse2.CompareEqual(Ssse3.Shuffle(asciiSpaceTbl.AsByte(), src).AsByte(), src.AsByte());
                // Movemask extract the MSB from each byte of asciispace
                // if the mask is not the same as the movemask extract, signal an error
                // Print the vectors and mask

                error |= mask != Sse2.MoveMask(asciiSpace.AsDouble());
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
                return;
            }

            // this particular implementation was inspired by work done by @animetosho
            // we do it in two steps, first 8 bytes and then second 8 bytes
            byte mask1 = (byte)mask;      // least significant 8 bits
            byte mask2 = (byte)(mask >> 8); // most significant 8 bits
            // next line just loads the 64-bit values thintable_epi8[mask1] and
            // thintable_epi8[mask2] into a 128-bit register, using only
            // two instructions on most compilers.

            ulong value1 = Tables.thintableEpi8[mask1]; // Adjust according to actual implementation
            ulong value2 = Tables.thintableEpi8[mask2]; // Adjust according to actual implementation

            Vector128<sbyte> shufmask = Vector128.Create(value2, value1).AsSByte();    

            // Increment by 0x08 the second half of the mask
            shufmask = Sse2.Add(shufmask, Vector128.Create(0x08080808, 0x08080808, 0, 0).AsSByte());

            // this is the version "nearly pruned"
            Vector128<sbyte> pruned = Ssse3.Shuffle(data.AsSByte(), shufmask);
            // we still need to put the two halves together.
            // we compute the popcount of the first half:
            int pop1 = Tables.BitsSetTable256mul2[mask1];
            // then load the corresponding mask, what it does is to write
            // only the first pop1 bytes from the first 8 bytes, and then
            // it fills in with the bytes from the second 8 bytes + some filling
            // at the end.
            
            fixed (byte* tablePtr = Tables.pshufbCombineTable)
            {
                Vector128<byte> compactmask = Sse2.LoadVector128(tablePtr + pop1 * 8);

                Vector128<byte> answer = Ssse3.Shuffle(pruned.AsByte(), compactmask);
                Sse2.Store(output, answer);            
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
            byte[] buffer = new byte[16];
            Span<byte> bufferSpan = buffer; // Convert byte array to Span<byte>
            
            // Safe memory copy for the last part of the data
            fixed (byte* bufferPtr = buffer)
            {
                    Base64Decode(bufferPtr, b->chunk3);
                Buffer.MemoryCopy(bufferPtr, outPtr + 36, 12, 12);
            }
        }

        public unsafe static void Base64Decode(byte* output, Vector128<byte> input)
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



                // round up to the nearest multiple of 4, then multiply by 3
                int decoded3bitsChunksToProcess = (bytesToProcess + 3) / 4 * 3;

                byte* endOfSafe64ByteZone =
                    decoded3bitsChunksToProcess >= 63 ? 
                            dst + decoded3bitsChunksToProcess - 63 :
                            dst;

                const int blocksSize = 6;
                Debug.Equals(blocksSize >= 2, "block should of size 2 or more");
                byte[] buffer = new byte[blocksSize * 64];
                fixed (byte* startOfBuffer = buffer)
                {
                    byte* bufferPtr = startOfBuffer;

                    if (bytesToProcess >= 64) //Start the main routine if there is atleast 64 bits (one block)
                    {

                        byte* srcEnd64 = srcInit + bytesToProcess - 64;
                        while (src <= srcEnd64)
                        {

                            Base64.Block64 b;
                            Base64.LoadBlock(&b, src);
                            src += 64;
                            bool error = false;
                            UInt64 badCharMask = Base64.ToBase64Mask(isUrl, &b, out error);
                            if (error)
                            {

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
                            if (bufferPtr >= (blocksSize - 1) * 64 + startOfBuffer) // We treat the last block separately later on
                            {

                                for (int i = 0; i < (blocksSize - 2); i++) // We also treat the second to last block differently! Until then it is safe to proceed:
                                {
                                    Base64DecodeBlock(dst, startOfBuffer + i * 64);


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
                                Buffer.MemoryCopy(bufferPtr + (blocksSize - 1) * 64, bufferPtr, 64, 64);
                                bufferPtr -= (blocksSize - 1) * 64;
                            }
                        }
                    }

                    // Optimization note: if this is almost full, then it is worth our
                    // time, otherwise, we should just decode directly.
                    int lastBlock = (int)((bufferPtr - startOfBuffer) % 64);
                    // Recall : a block is 6* 64 bits
                    // There is at some bytes remaining beyond the last 64 bit block remaining
                    if (lastBlock != 0 && srcEnd - src + lastBlock >= 64) // We first check if there is any error and eliminate white spaces?:
                    {

                        while ((bufferPtr - startOfBuffer) % 64 != 0 && src < srcEnd)
                        {
                            int whereWeAre = (int)(src - srcInit);
                            // Corrected syntax for string interpolation

                            byte val = toBase64[(int)*src];
                            *bufferPtr = val;
                            if (val > 64)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.InvalidData;
                            }
                            // TODO/EXPLAIN: We do not advance the bufferPtr if val = 64 , white spaces?
                            bufferPtr += (val <= 63) ? 1 : 0;
                            src++; // TODO/DEBUG: This looks sus?
                        }


                    }


                    // there is none, we can proceed:
                    byte* subBufferPtr = startOfBuffer;
                    for (; subBufferPtr + 64 <= bufferPtr; subBufferPtr += 64) //  decode by chunks of 64 bits blocks
                    {

                        if (dst >= endOfSafe64ByteZone) // check if there is enough room in the destination
                        {
                            Base64DecodeBlockSafe(dst, subBufferPtr);
                        }
                        else
                        {
                            Base64DecodeBlock(dst, subBufferPtr);
                        }

                        dst += 48;// 64 bits of base64 decodes to 48 bits 
                    }
                    if ((bufferPtr - subBufferPtr) % 64 != 0) // after decoding the chunks, thers still bits remaining in the buffer
                    {


                        while (subBufferPtr + 4 < bufferPtr) // we decode one base64 element (4 bit) at a time
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
                        if (subBufferPtr + 4 <= bufferPtr) // this may be the very last element, might be incompletep
                        // TODO: This condition doesnt seem to be strictly nescessary for the code to be correct and prolly could be rolled into the loop above
                        // To check benchmark
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

                            // we check each leftover byte one by one for error
                            while (leftover < 4 && src < srcEnd) //makes sure we dont go over boundaries
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
                    if (src < srcEnd + equalsigns) // We finished processing 64-bit blocks
                    {

                        // bytesConsumed = (int)(src - srcInit);
                        // bytesWritten = (int)(dst - dstInit);

                        int sourceIndex = Math.Max(0, (int)(src - srcInit));
                        int destIndex = Math.Max(0, (int)(dst - dstInit));

                        bytesConsumed = Math.Min(sourceIndex, source.Length);
                        bytesWritten = Math.Min(destIndex, dest.Length);



                        int remainderBytesConsumed = 0;
                        int remainderBytesWritten = 0;

                        OperationStatus result =
                            // Base64WithWhiteSpaceToBinaryScalar(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);
                            SafeBase64ToBinaryWithWhiteSpace(source.Slice(bytesConsumed), dest.Slice(bytesWritten), out remainderBytesConsumed, out remainderBytesWritten, isUrl);
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
