using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.Buffers.Binary;

namespace SimdBase64
{
    namespace Scalar
    {
        public static partial class Base64
        {
            public enum Endianness
            {
                LITTLE = 0,
                BIG = 1
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool MatchSystem(Endianness e)
            {
                return e == (BitConverter.IsLittleEndian ? Endianness.LITTLE : Endianness.BIG);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool IsAsciiWhiteSpace(byte c)
            {
                ReadOnlySpan<bool> table = new bool[] {
            false, false, false, false, false, false, false, false, false, true, true, false, true, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false};
                return Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(table), (nint)c);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static bool IsAsciiWhiteSpace(char c)
            {
                if (c > 127)
                {
                    return false;
                }
                return IsAsciiWhiteSpace((byte)c);
            }


            [Flags]
            public enum Base64Options
            {
                None = 0, // Standard base64 format
                Url = 1      // Base64url format
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int MaximalBinaryLengthFromBase64Scalar<T>(ReadOnlySpan<T> input)
            {
                // We follow https://infra.spec.whatwg.org/#forgiving-base64-decode
                int length = input.Length;
                if (length % 4 <= 1)
                {
                    return length / 4 * 3;
                }
                // If we have a valid input, then the remainder must be 2 or 3 adding one or two extra bytes.
                return length / 4 * 3 + (length % 4) - 1;
            }

            public unsafe static OperationStatus DecodeFromBase64Scalar(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {

                int length = source.Length;

                fixed (byte* srcInit = source)
                fixed (byte* dstInit = dest)
                {
                    byte* srcEnd = srcInit + length;
                    byte* src = srcInit;
                    byte* dst = dstInit;

                    UInt32 x;
                    uint triple;
                    int idx;
                    // Should be
                    // Span<byte> buffer = stackalloc byte[4];
                    Span<byte> buffer = [0, 0, 0, 0];

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

                            *(uint*)dst = x;// optimization opportunity: copy 4 bytes
                            dst += 3;
                            src += 4;
                        }
                        idx = 0;

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

                        // deals with reminder
                        if (idx != 4)
                        {
                            if (idx == 2) // we just copy directly while converting
                            {
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

                            else if (idx == 3)
                            {
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
                                return OperationStatus.NeedMoreData;// The base64 input terminates with a single character, excluding padding.
                            }
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.Done;
                        }
                        triple =
                            ((uint)buffer[0] << 3 * 6) + ((uint)buffer[1] << 2 * 6) +
                            ((uint)buffer[2] << 1 * 6) + ((uint)buffer[3] << 0 * 6);
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsValidBase64Index(char b)
            {
                return b < 256;
            }

            public unsafe static OperationStatus DecodeFromBase64Scalar(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {

                int length = source.Length;

                fixed (char* srcInit = source)
                fixed (byte* dstInit = dest)

                {
                    char* srcEnd = srcInit + length;
                    char* src = srcInit;
                    byte* dst = dstInit;

                    UInt32 x;
                    uint triple;
                    int idx;
                    // Should be
                    // Span<byte> buffer = stackalloc byte[4];
                    Span<byte> buffer = [0, 0, 0, 0];

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

                            *(uint*)dst = x;// optimization opportunity: copy 4 bytes
                            dst += 3;
                            src += 4;
                        }
                        idx = 0;

                        // We need at least four characters.
                        while (idx < 4 && src < srcEnd)
                        {
                            char c = (char)*src;
                            if (!IsValidBase64Index(c)) // Ensure c is a valid index
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);

                                return OperationStatus.InvalidData;
                                // Process code
                            }
                            byte code = isUrl ? Tables.GetToBase64UrlValue(c) : Tables.GetToBase64Value(c);
                            // byte code = 1;
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

                        // deals with reminder
                        if (idx != 4)
                        {
                            if (idx == 2) // we just copy directly while converting
                            {
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

                            else if (idx == 3)
                            {
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
                                return OperationStatus.NeedMoreData;// The base64 input terminates with a single character, excluding padding.
                            }
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.Done;
                        }
                        triple =
                            ((uint)buffer[0] << 3 * 6) + ((uint)buffer[1] << 2 * 6) +
                            ((uint)buffer[2] << 1 * 6) + ((uint)buffer[3] << 0 * 6);
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


            public static OperationStatus Base64WithWhiteSpaceToBinaryScalar(ReadOnlySpan<byte> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                int length = input.Length;
                int whiteSpaces = 0;
                while (length > 0 && IsAsciiWhiteSpace((char)input[length - 1]))
                {
                    length--;
                    whiteSpaces++;
                }
                int equallocation = length; // location of the first padding character if any
                int equalsigns = 0;
                if (length > 0 && input[length - 1] == '=')
                {
                    length -= 1;
                    equalsigns++;
                    while (length > 0 && IsAsciiWhiteSpace((char)input[length - 1]))
                    {
                        length--;
                        whiteSpaces++;
                    }
                    if (length > 0 && input[length - 1] == '=')
                    {
                        equalsigns++;
                        length -= 1;
                    }
                }
                if (length == 0)
                {
                    if (equalsigns > 0)
                    {
                        bytesConsumed = equallocation;
                        bytesWritten = 0;

                        return OperationStatus.InvalidData;

                    }
                    bytesConsumed = 0 + whiteSpaces + equalsigns;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                ReadOnlySpan<byte> trimmedInput = input.Slice(0, length);

                OperationStatus r = Base64.DecodeFromBase64Scalar(trimmedInput, output, out bytesConsumed, out bytesWritten, isUrl);

                if (r == OperationStatus.Done)
                {
                    if (equalsigns > 0)
                    {
                        // Additional checks
                        if ((bytesWritten % 3 == 0) || (((bytesWritten % 3) + 1 + equalsigns) != 4))
                        {
                            return OperationStatus.InvalidData;
                        }
                    }

                    // Only increment bytesConsumed if decoding was successful
                    bytesConsumed += equalsigns + whiteSpaces;
                }
                return r;
            }

            public static OperationStatus Base64WithWhiteSpaceToBinaryScalar(ReadOnlySpan<char> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
            {
                int length = input.Length;
                int whiteSpaces = 0;
                while (length > 0 && IsAsciiWhiteSpace((char)input[length - 1]))
                {
                    length--;
                    whiteSpaces++;
                }
                int equallocation = length; // location of the first padding character if any
                int equalsigns = 0;
                if (length > 0 && input[length - 1] == '=')
                {
                    length -= 1;
                    equalsigns++;
                    while (length > 0 && IsAsciiWhiteSpace((char)input[length - 1]))
                    {
                        length--;
                        whiteSpaces++;
                    }
                    if (length > 0 && input[length - 1] == '=')
                    {
                        equalsigns++;
                        length -= 1;
                    }
                }
                if (length == 0)
                {
                    if (equalsigns > 0)
                    {
                        bytesConsumed = equallocation;
                        bytesWritten = 0;

                        return OperationStatus.InvalidData;

                    }
                    bytesConsumed = 0 + whiteSpaces + equalsigns;
                    bytesWritten = 0;
                    return OperationStatus.Done;
                }

                ReadOnlySpan<char> trimmedInput = input.Slice(0, length);

                OperationStatus r = Base64.DecodeFromBase64Scalar(trimmedInput, output, out bytesConsumed, out bytesWritten, isUrl);

                if (r == OperationStatus.Done)
                {
                    if (equalsigns > 0)
                    {
                        // Additional checks
                        if ((bytesWritten % 3 == 0) || (((bytesWritten % 3) + 1 + equalsigns) != 4))
                        {
                            return OperationStatus.InvalidData;
                        }
                    }

                    // Only increment bytesConsumed if decoding was successful
                    bytesConsumed += equalsigns + whiteSpaces;
                }
                return r;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Base64LengthFromBinary(int length, bool isUrl = false)
            {
                if (isUrl)
                {
                    return length / 3 * 4 + (length % 3 != 0 ? length % 3 + 1 : 0);
                }
                // Standard Base64 length calculation with padding to make the length a multiple of 4
                return (length + 2) / 3 * 4;
            }
        }
    }
}

