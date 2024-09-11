// These 'safe' methods are designed to be used when the output buffer is not large enough to hold the entire decoded data.
// We only use them in our tests.
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace SimdBase64
{
    namespace Scalar
    {
        public static partial class Base64
        {
            // like DecodeFromBase64Scalar, but it will not write past the end of the ouput buffer.
            public unsafe static OperationStatus SafeDecodeFromBase64Scalar(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                int length = source.Length;
                Span<byte> buffer = [0, 0, 0, 0];

                // Define pointers within the fixed blocks
                fixed (byte* srcInit = source)
                fixed (byte* dstInit = dest)
                fixed (byte* bufferPtr = buffer)
                {
                    byte* srcEnd = srcInit + length;
                    byte* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    // Continue the implementation
                    uint x;
                    uint triple;
                    int idx;
                    // Should be
                    // Span<byte> buffer = stackalloc byte[4];

                    while (true)
                    {
                        // fastpath
                        while (src + 4 <= srcEnd &&
                               (x = isUrl ? Base64Url.GetD(src) : Base64Default.GetD(src)) < 0x01FFFFFF)
                        {


                            if (MatchSystem(Endianness.BIG))
                            {
                                x = BinaryPrimitives.ReverseEndianness(x);
                            }
                            if (dst + 3 > dstEnd)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.DestinationTooSmall;
                            }
                            Buffer.MemoryCopy(bufferPtr, dst, 3, 3);
                            dst += 3;
                            src += 4;
                        }
                        idx = 0;

                        byte* srcCurrent = src;

                        // We need at least four characters.
                        while (idx < 4 && src < srcEnd)
                        {
                            char c = (char)*src;
                            byte code = isUrl ? Tables.GetToBase64UrlValue(c) : Tables.GetToBase64Value(c);
                            buffer[idx] = code;

                            if (code <= 63)
                            {
                                idx++;
                            }
                            else if (code > 64)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.InvalidData;// Found a character that cannot be part of a valid base64 string.
                            }
                            else
                            {
                                // We have a space or a newline. We ignore it.
                            }
                            src++;
                        }

                        // deals with remainder
                        if (idx != 4)
                        {
                            if (idx == 2) // we just copy directly while converting
                            {
                                if (dst == dstEnd)
                                {
                                    bytesConsumed = (int)(srcCurrent - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.DestinationTooSmall;
                                }
                                triple = ((uint)buffer[0] << (3 * 6)) + ((uint)buffer[1] << (2 * 6)); // the 2 last byte are shifted 18 and 12 bits respectively
                                if (MatchSystem(Endianness.BIG))
                                {
                                    triple <<= 8;
                                    byte[] byteTriple = BitConverter.GetBytes(triple);
                                    dst[0] = byteTriple[0];
                                }
                                else
                                {
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 1, 1);
                                }
                                dst += 1;
                            }

                            else if (idx == 3) // same story here
                            {
                                if (dst + 2 > dstEnd)
                                {
                                    bytesConsumed = (int)(srcCurrent - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.DestinationTooSmall;
                                }
                                triple = ((uint)buffer[0] << 3 * 6) +
                                                ((uint)buffer[1] << 2 * 6) +
                                                ((uint)buffer[2] << 1 * 6);
                                if (MatchSystem(Endianness.BIG))
                                {
                                    triple <<= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                }
                                else
                                {
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                }
                                dst += 2;
                            }

                            else if (idx == 1)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);

                                return OperationStatus.InvalidData;// The base64 input terminates with a single character, excluding padding.
                            }
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.Done;//SUCCESS
                        }

                        if (dst + 3 >= dstEnd)
                        {
                            bytesConsumed = (int)(srcCurrent - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.DestinationTooSmall;
                        }
                        triple =
                            ((uint)(buffer[0]) << 3 * 6) + ((uint)(buffer[1]) << 2 * 6) +
                            ((uint)(buffer[2]) << 1 * 6) + ((uint)(buffer[3]) << 0 * 6);
                        if (MatchSystem(Endianness.BIG))
                        {
                            triple <<= 8;
                            Buffer.MemoryCopy(&triple, dst, 3, 3);
                        }
                        else
                        {
                            triple = BinaryPrimitives.ReverseEndianness(triple);
                            triple >>= 8;
                            Buffer.MemoryCopy(&triple, dst, 3, 3);
                        }
                        dst += 3;
                    }

                }
            }

            // like DecodeFromBase64Scalar, but it will not write past the end of the ouput buffer.
            public unsafe static OperationStatus SafeDecodeFromBase64Scalar(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {

                int length = source.Length;

                // Should be
                // Span<byte> buffer = stackalloc byte[4];
                Span<byte> buffer = [0, 0, 0, 0];
                // Define pointers within the fixed blocks
                fixed (char* srcInit = source)
                fixed (byte* dstInit = dest)
                fixed (byte* bufferPtr = buffer)

                {
                    char* srcEnd = srcInit + length;
                    char* src = srcInit;
                    byte* dst = dstInit;
                    byte* dstEnd = dstInit + dest.Length;

                    // Continue the implementation
                    uint x;
                    uint triple;
                    int idx;

                    while (true)
                    {
                        // fastpath
                        while (src + 4 <= srcEnd &&
                               (x = isUrl ? Base64Url.GetD(src) : Base64Default.GetD(src)) < 0x01FFFFFF)
                        {
                            if (MatchSystem(Endianness.BIG))
                            {
                                x = BinaryPrimitives.ReverseEndianness(x);
                            }
                            if (dst + 3 > dstEnd)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.DestinationTooSmall;
                            }
                            Buffer.MemoryCopy(bufferPtr, dst, 3, 3);
                            dst += 3;
                            src += 4;
                        }
                        idx = 0;

                        char* srcCurrent = src;

                        // We need at least four characters.
                        while (idx < 4 && src < srcEnd)
                        {
                            char c = (char)*src;
                            byte code = isUrl ? Tables.GetToBase64UrlValue(c) : Tables.GetToBase64Value(c);
                            buffer[idx] = code;

                            if (code <= 63)
                            {
                                idx++;
                            }
                            else if (code > 64)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.InvalidData;// Found a character that cannot be part of a valid base64 string.
                            }
                            else
                            {
                                // We have a space or a newline. We ignore it.
                            }
                            src++;
                        }

                        // deals with remainder
                        if (idx != 4)
                        {
                            if (idx == 2) // we just copy directly while converting
                            {
                                if (dst == dstEnd)
                                {
                                    bytesConsumed = (int)(srcCurrent - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.DestinationTooSmall;
                                }
                                triple = ((uint)buffer[0] << (3 * 6)) + ((uint)buffer[1] << (2 * 6)); // the 2 last byte are shifted 18 and 12 bits respectively
                                if (MatchSystem(Endianness.BIG))
                                {
                                    triple <<= 8;
                                    byte[] byteTriple = BitConverter.GetBytes(triple);
                                    dst[0] = byteTriple[0];
                                }
                                else
                                {
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 1, 1);
                                }
                                dst += 1;
                            }

                            else if (idx == 3) // same story here
                            {
                                if (dst + 2 > dstEnd)
                                {
                                    bytesConsumed = (int)(srcCurrent - srcInit);
                                    bytesWritten = (int)(dst - dstInit);
                                    return OperationStatus.DestinationTooSmall;
                                }
                                triple = ((uint)buffer[0] << 3 * 6) +
                                                ((uint)buffer[1] << 2 * 6) +
                                                ((uint)buffer[2] << 1 * 6);
                                if (MatchSystem(Endianness.BIG))
                                {
                                    triple <<= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                }
                                else
                                {
                                    triple = BinaryPrimitives.ReverseEndianness(triple);
                                    triple >>= 8;
                                    Buffer.MemoryCopy(&triple, dst, 2, 2);
                                }
                                dst += 2;
                            }

                            else if (idx == 1)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);

                                return OperationStatus.InvalidData;// The base64 input terminates with a single character, excluding padding.
                            }
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.Done;//SUCCESS
                        }

                        if (dst + 3 >= dstEnd)
                        {
                            bytesConsumed = (int)(srcCurrent - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.DestinationTooSmall;
                        }
                        triple =
                            ((uint)(buffer[0]) << 3 * 6) + ((uint)(buffer[1]) << 2 * 6) +
                            ((uint)(buffer[2]) << 1 * 6) + ((uint)(buffer[3]) << 0 * 6);
                        if (MatchSystem(Endianness.BIG))
                        {
                            triple <<= 8;
                            Buffer.MemoryCopy(&triple, dst, 3, 3);
                        }
                        else
                        {
                            triple = BinaryPrimitives.ReverseEndianness(triple);
                            triple >>= 8;
                            Buffer.MemoryCopy(&triple, dst, 3, 3);
                        }
                        dst += 3;
                    }

                }
            }


            public unsafe static OperationStatus SafeBase64ToBinaryWithWhiteSpace(ReadOnlySpan<byte> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                // The implementation could be nicer, but we expect that most times, the user
                // will provide us with a buffer that is large enough.
                int maxLength = MaximalBinaryLengthFromBase64Scalar(input);

                if (output.Length >= maxLength)
                {
                    // fast path
                    OperationStatus fastPathResult = Base64.Base64WithWhiteSpaceToBinaryScalar(input, output, out bytesConsumed, out bytesWritten, isUrl);
                    return fastPathResult;
                }
                // The output buffer is maybe too small. We will decode a truncated version of the input.
                int outlen3 = output.Length / 3 * 3; // round down to multiple of 3
                int safeInputLength = Base64LengthFromBinary(outlen3);
                OperationStatus r = DecodeFromBase64Scalar(input.Slice(0, Math.Max(0, safeInputLength)), output, out bytesConsumed, out bytesWritten, isUrl); // there might be a -1 error here
                if (r == OperationStatus.InvalidData)
                {
                    return r;
                }
                int offset = (r == OperationStatus.NeedMoreData) ? 1 :
                    ((bytesWritten % 3) == 0 ?
                            0 : (bytesWritten % 3) + 1);

                int outputIndex = bytesWritten - (bytesWritten % 3);
                int inputIndex = safeInputLength;
                int whiteSpaces = 0;
                // offset is a value that is no larger than 3. We backtrack
                // by up to offset characters + an undetermined number of
                // white space characters. It is expected that the next loop
                // runs at most 3 times + the number of white space characters
                // in between them, so we are not worried about performance.
                while (offset > 0 && inputIndex > 0)
                {
                    char c = (char)input[--inputIndex];
                    if (IsAsciiWhiteSpace(c))
                    {
                        // skipping
                    }
                    else
                    {
                        offset--;
                        whiteSpaces++;
                    }
                }
                ReadOnlySpan<byte> tailInput = input.Slice(inputIndex);
                int RemainingInputLength = tailInput.Length;
                while (RemainingInputLength > 0 && IsAsciiWhiteSpace((char)tailInput[RemainingInputLength - 1]))
                {
                    RemainingInputLength--;
                }
                int paddingCharacts = 0;
                if (RemainingInputLength > 0 && tailInput[RemainingInputLength - 1] == '=')
                {
                    RemainingInputLength--;
                    paddingCharacts++;
                    while (RemainingInputLength > 0 && IsAsciiWhiteSpace((char)tailInput[RemainingInputLength - 1]))
                    {
                        RemainingInputLength--;
                        whiteSpaces++;
                    }
                    if (RemainingInputLength > 0 && tailInput[RemainingInputLength - 1] == '=')
                    {
                        RemainingInputLength--;
                        paddingCharacts++;
                    }
                }

                int tailBytesConsumed;
                int tailBytesWritten;

                Span<byte> remainingOut = output.Slice(Math.Min(output.Length, outputIndex));
                r = SafeDecodeFromBase64Scalar(tailInput.Slice(0, RemainingInputLength), remainingOut, out tailBytesConsumed, out tailBytesWritten, isUrl);
                if (r == OperationStatus.Done && paddingCharacts > 0)
                {
                    // additional checks:
                    if ((remainingOut.Length % 3 == 0) || ((remainingOut.Length % 3) + 1 + paddingCharacts != 4))
                    {
                        r = OperationStatus.InvalidData;
                    }
                }


                if (r == OperationStatus.Done)
                {
                    bytesConsumed += tailBytesConsumed + paddingCharacts + whiteSpaces;
                }
                else
                {
                    bytesConsumed += tailBytesConsumed;
                }
                bytesWritten += tailBytesWritten;
                return r;
            }

            public unsafe static OperationStatus SafeBase64ToBinaryWithWhiteSpace(ReadOnlySpan<char> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                // The implementation could be nicer, but we expect that most times, the user
                // will provide us with a buffer that is large enough.
                int maxLength = MaximalBinaryLengthFromBase64Scalar(input);

                if (output.Length >= maxLength)
                {
                    // fast path
                    OperationStatus fastPathResult = Base64.Base64WithWhiteSpaceToBinaryScalar(input, output, out bytesConsumed, out bytesWritten, isUrl);
                    return fastPathResult;
                }
                // The output buffer is maybe too small. We will decode a truncated version of the input.
                int outlen3 = output.Length / 3 * 3; // round down to multiple of 3
                int safeInputLength = Base64LengthFromBinary(outlen3);
                OperationStatus r = DecodeFromBase64Scalar(input.Slice(0, Math.Max(0, safeInputLength)), output, out bytesConsumed, out bytesWritten, isUrl); // there might be a -1 error here
                if (r == OperationStatus.InvalidData)
                {
                    return r;
                }
                int offset = (r == OperationStatus.NeedMoreData) ? 1 :
                    ((bytesWritten % 3) == 0 ?
                            0 : (bytesWritten % 3) + 1);

                int outputIndex = bytesWritten - (bytesWritten % 3);
                int inputIndex = safeInputLength;
                int whiteSpaces = 0;
                // offset is a value that is no larger than 3. We backtrack
                // by up to offset characters + an undetermined number of
                // white space characters. It is expected that the next loop
                // runs at most 3 times + the number of white space characters
                // in between them, so we are not worried about performance.
                while (offset > 0 && inputIndex > 0)
                {
                    char c = (char)input[--inputIndex];
                    if (IsAsciiWhiteSpace(c))
                    {
                        // skipping
                    }
                    else
                    {
                        offset--;
                        whiteSpaces++;
                    }
                }
                ReadOnlySpan<char> tailInput = input.Slice(inputIndex);
                int RemainingInputLength = tailInput.Length;
                while (RemainingInputLength > 0 && IsAsciiWhiteSpace((char)tailInput[RemainingInputLength - 1]))
                {
                    RemainingInputLength--;
                }
                int paddingCharacts = 0;
                if (RemainingInputLength > 0 && tailInput[RemainingInputLength - 1] == '=')
                {
                    RemainingInputLength--;
                    paddingCharacts++;
                    while (RemainingInputLength > 0 && IsAsciiWhiteSpace((char)tailInput[RemainingInputLength - 1]))
                    {
                        RemainingInputLength--;
                        whiteSpaces++;
                    }
                    if (RemainingInputLength > 0 && tailInput[RemainingInputLength - 1] == '=')
                    {
                        RemainingInputLength--;
                        paddingCharacts++;
                    }
                }

                int tailBytesConsumed;
                int tailBytesWritten;

                Span<byte> remainingOut = output.Slice(Math.Min(output.Length, outputIndex));
                r = SafeDecodeFromBase64Scalar(tailInput.Slice(0, RemainingInputLength), remainingOut, out tailBytesConsumed, out tailBytesWritten, isUrl);
                if (r == OperationStatus.Done && paddingCharacts > 0)
                {
                    // additional checks:
                    if ((remainingOut.Length % 3 == 0) || ((remainingOut.Length % 3) + 1 + paddingCharacts != 4))
                    {
                        r = OperationStatus.InvalidData;
                    }
                }


                if (r == OperationStatus.Done)
                {
                    bytesConsumed += tailBytesConsumed + paddingCharacts + whiteSpaces;
                }
                else
                {
                    bytesConsumed += tailBytesConsumed;
                }
                bytesWritten += tailBytesWritten;
                return r;
            }

        }
    }
}