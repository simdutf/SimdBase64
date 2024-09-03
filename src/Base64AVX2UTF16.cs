using System;
using System.Buffers;
using System.Buffers.Binary;

namespace SimdBase64
{
    namespace AVX2
    {
        public static partial class Base64
        {
            // Caller is responsible for checking that Avx2.IsSupported && Popcnt.IsSupported
            public unsafe static OperationStatus DecodeFromBase64AVX2(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {


                if (isUrl)
                {
                    return InnerDecodeFromBase64AVX2Url(source, dest, out bytesConsumed, out bytesWritten);
                }
                else
                {
                    return InnerDecodeFromBase64AVX2Regular(source, dest, out bytesConsumed, out bytesWritten);
                }
            }

            private unsafe static OperationStatus InnerDecodeFromBase64AVX2Regular(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
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
                            char* srcEnd64 = srcInit + bytesToProcess - 64;
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
                        int lastBlockSrcCount = 0;
                        // There is at some bytes remaining beyond the last 64 bit block remaining
                        if (lastBlock != 0 && srcEnd - src + lastBlock >= 64) // We first check if there is any error and eliminate white spaces?:
                        {
                            while ((bufferPtr - startOfBuffer) % 64 != 0 && src < srcEnd)
                            {
                                if (!SimdBase64.Scalar.Base64.IsValidBase64Index(*src))
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
                                byte val = toBase64[(int)*src];
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
                                    if (!SimdBase64.Scalar.Base64.IsValidBase64Index(*src))
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

            private unsafe static OperationStatus InnerDecodeFromBase64AVX2Url(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten)
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
                            char* srcEnd64 = srcInit + bytesToProcess - 64;
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

                                if (!SimdBase64.Scalar.Base64.IsValidBase64Index(*src))
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
                                byte val = toBase64[(int)*src];
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

                                if (!SimdBase64.Scalar.Base64.IsValidBase64Index(*src))
                                {
                                    bytesConsumed = (int)(src - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.InvalidData;
                                }


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
