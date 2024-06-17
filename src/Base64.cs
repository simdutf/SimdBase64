using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO.Pipes;

namespace SimdUnicode
{
    public static class Base64
    {
        public enum Endianness
        {
            LITTLE = 0,
            BIG = 1
        }
        public static bool MatchSystem(Endianness e)
        {
            return e == (BitConverter.IsLittleEndian ? Endianness.LITTLE : Endianness.BIG);
        }

        public static bool IsAsciiWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }

        [Flags]
        public enum Base64Options
        {
            None = 0, // Standard base64 format
            Url = 1      // Base64url format
        }

        public static uint SwapBytes(uint x)
        {
            // Swap bytes implementation to handle endianess
            return ((x & 0x000000FF) << 24) |
                   ((x & 0x0000FF00) << 8) |
                   ((x & 0x00FF0000) >> 8) |
                   ((x & 0xFF000000) >> 24);
        }

        public static int MaximalBinaryLengthFromBase64Scalar(ReadOnlySpan<byte> input)
        {
            // We follow https://infra.spec.whatwg.org/#forgiving-base64-decode
            int padding = 0;
            int length = input.Length;
            if (length > 0)
            {
                if (input[length - 1].Equals('='))
                {
                    padding++;
                    if (length > 1 && input[length - 2].Equals('='))
                    {

                        padding++;
                    }
                }
            }
            int actualLength = length - padding;
            if (actualLength % 4 <= 1)
            {
                return actualLength / 4 * 3;
            }
            // If we have a valid input, then the remainder must be 2 or 3 adding one or two extra bytes.
            return actualLength / 4 * 3 + (actualLength % 4) - 1;
        }

        public unsafe static OperationStatus DecodeFromBase64Scalar(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true, bool isUrl = false)
        {


            byte[] toBase64 = isUrl != false ? Base64Tables.tables.ToBase64UrlValue : Base64Tables.tables.ToBase64Value;
            uint[] d0 = isUrl != false ? Base64Tables.tables.Url.d0 : Base64Tables.tables.Default.d0;
            uint[] d1 = isUrl != false ? Base64Tables.tables.Url.d1 : Base64Tables.tables.Default.d1;
            uint[] d2 = isUrl != false ? Base64Tables.tables.Url.d2 : Base64Tables.tables.Default.d2;
            uint[] d3 = isUrl != false ? Base64Tables.tables.Url.d3 : Base64Tables.tables.Default.d3;

            int length = source.Length;

            fixed (byte* srcInit = source)
            fixed (byte* dstInit = dest)

            {
                byte* srcEnd = srcInit + length;
                byte* src = srcInit;
                byte* dst = dstInit;

                uint x;
                uint triple;
                int idx;
                byte[] buffer = new byte[4];

                while (true)
                {
                    // fastpath
                    while (src + 4 <= srcEnd &&
                           (x = d0[*src] | d1[src[1]] | d2[src[2]] | d3[src[3]]) < 0x01FFFFFF)
                    {
                        if (MatchSystem(Endianness.BIG))
                        {
                            x = SwapBytes(x);
                        }
                        Marshal.Copy(buffer, 0, (IntPtr)dst, 3); // optimization opportunity: copy 4 bytes
                        dst += 3;
                        src += 4;
                    }
                    idx = 0;

                    // We need at least four characters.
                    while (idx < 4 && src < srcEnd)
                    {
                        char c = (char)*src;

                        byte code = toBase64[c];
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
                                triple = SwapBytes(triple);
                                triple >>= 8;
                                byte[] byteTriple = BitConverter.GetBytes(triple);
                                dst[0] = byteTriple[0];  // Copy only the first byte
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
                                Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 2);
                            }
                            else
                            {
                                triple = SwapBytes(triple);
                                triple >>= 8;
                                Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 2);
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
                        Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);

                    }
                    else
                    {
                        triple = SwapBytes(triple);
                        triple >>= 8;
                        Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);
                    }
                    dst += 3;
                }

            }
        }

        // like DecodeFromBase64Scalar, but it will not write past the end of the ouput buffer.
        public unsafe static OperationStatus SafeDecodeFromBase64Scalar(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true, bool isUrl = false)
        {
            byte[] toBase64 = isUrl != false ? Base64Tables.tables.ToBase64UrlValue : Base64Tables.tables.ToBase64Value;
            uint[] d0 = isUrl != false ? Base64Tables.tables.Url.d0 : Base64Tables.tables.Default.d0;
            uint[] d1 = isUrl != false ? Base64Tables.tables.Url.d1 : Base64Tables.tables.Default.d1;
            uint[] d2 = isUrl != false ? Base64Tables.tables.Url.d2 : Base64Tables.tables.Default.d2;
            uint[] d3 = isUrl != false ? Base64Tables.tables.Url.d3 : Base64Tables.tables.Default.d3;

            int length = source.Length;

            // Define pointers within the fixed blocks
            fixed (byte* srcInit = source)
            fixed (byte* dstInit = dest)

            {
                byte* srcEnd = srcInit + length;
                byte* src = srcInit;
                byte* dst = dstInit;
                byte* dstEnd = dstInit + dest.Length;

                // Continue the implementation
                uint x;
                uint triple;
                int idx;
                byte[] buffer = new byte[4];

                while (true)
                {
                    // fastpath
                    while (src + 4 <= srcEnd &&
                           (x = d0[*src] | d1[src[1]] | d2[src[2]] | d3[src[3]]) < 0x01FFFFFF)
                    {
                        if (MatchSystem(Endianness.BIG))
                        {
                            x = SwapBytes(x);
                        }
                        if (dst + 3 > dstEnd)
                        {
                            bytesConsumed = (int)(src - srcInit);
                            bytesWritten = (int)(dst - dstInit);
                            return OperationStatus.DestinationTooSmall;
                        }
                        Marshal.Copy(buffer, 0, (IntPtr)dst, 3); // optimization opportunity: copy 4 bytes
                        dst += 3;
                        src += 4;
                    }
                    idx = 0;

                    // We need at least four characters.
                    while (idx < 4 && src < srcEnd)
                    {

                        char c = (char)*src;
                        byte code = toBase64[c];
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
                            if (dst == dstEnd)
                            {
                                bytesConsumed = (int)(src - srcInit);
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
                                triple = SwapBytes(triple);
                                triple >>= 8;
                                byte[] byteTriple = BitConverter.GetBytes(triple);
                                dst[0] = byteTriple[0];  // Copy only the first byte
                            }
                            dst += 1;
                        }

                        else if (idx == 3) // same story here
                        {
                            if (dst + 2 > dstEnd)
                            {
                                bytesConsumed = (int)(src - srcInit);
                                bytesWritten = (int)(dst - dstInit);
                                return OperationStatus.DestinationTooSmall;
                            }
                            triple = ((uint)buffer[0] << 3 * 6) +
                                            ((uint)buffer[1] << 2 * 6) +
                                            ((uint)buffer[2] << 1 * 6);
                            if (MatchSystem(Endianness.BIG))
                            {
                                triple <<= 8;
                                Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 2);
                            }
                            else
                            {
                                triple = SwapBytes(triple);
                                triple >>= 8;
                                Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 2);
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

                    if (dst + 3 > dstEnd)
                    {
                        bytesConsumed = (int)(src - srcInit);
                        bytesWritten = (int)(dst - dstInit);
                        return OperationStatus.DestinationTooSmall;
                    }
                    triple =
                        ((uint)(buffer[0]) << 3 * 6) + ((uint)(buffer[1]) << 2 * 6) +
                        ((uint)(buffer[2]) << 1 * 6) + ((uint)(buffer[3]) << 0 * 6);
                    if (MatchSystem(Endianness.BIG))
                    {
                        triple <<= 8;
                        Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);

                    }
                    else
                    {
                        triple = SwapBytes(triple);
                        triple >>= 8;
                        Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);
                    }
                    dst += 3;
                }

            }
        }


        public static OperationStatus Base64WithWhiteSpaceToBinaryScalar(ReadOnlySpan<byte> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true, bool isUrl = false)
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
                bytesConsumed = 0;
                bytesWritten = 0;
                return OperationStatus.Done;
            }

            ReadOnlySpan<byte> trimmedInput = input.Slice(0, length);
            OperationStatus r = Base64.DecodeFromBase64Scalar(trimmedInput, output, out bytesConsumed, out bytesWritten, isFinalBlock, isUrl);

            if (r == OperationStatus.Done && equalsigns > 0)
            {
                //  additional checks
                if ((bytesWritten % 3 == 0) || (((bytesWritten % 3) + 1 + equalsigns) != 4))
                { 
                    return OperationStatus.InvalidData;
                }
            }
            bytesConsumed += equalsigns + whiteSpaces;
            return r;
        }


        public static int Base64LengthFromBinary(int length, bool isUrl = false)
        {
            if (isUrl)
            {
                return length / 3 * 4 + (length % 3 != 0 ? length % 3 + 1 : 0);
            }
            // Standard Base64 length calculation with padding to make the length a multiple of 4
            return (length + 2) / 3 * 4;
        }

        public unsafe static OperationStatus SafeBase64ToBinaryWithWhiteSpace(ReadOnlySpan<byte> input, Span<byte> output, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true, bool isUrl = false)
        {
            // The implementation could be nicer, but we expect that most times, the user
            // will provide us with a buffer that is large enough.
            int maxLength = MaximalBinaryLengthFromBase64Scalar(input);

            if (output.Length >= maxLength)
            {
                // fast path
                OperationStatus fastPathResult = Base64.Base64WithWhiteSpaceToBinaryScalar(input, output, out bytesConsumed, out bytesWritten, isFinalBlock, isUrl);
                return fastPathResult;
            }
            // The output buffer is maybe too small. We will decode a truncated version of the input.
            int outlen3 = output.Length / 3 * 3; // round down to multiple of 3
            int safeInputLength = Base64LengthFromBinary(outlen3);
            OperationStatus r = DecodeFromBase64Scalar(input.Slice(0, safeInputLength - 1), output, out bytesConsumed, out bytesWritten, isFinalBlock, isUrl); // there might be a -1 error here


            if (r == OperationStatus.InvalidData)
            {
                return r;
            }
            int offset = (r == OperationStatus.NeedMoreData) ? 1 :
                ((bytesConsumed % 3) == 0 ?
                        0 : (bytesConsumed % 3) + 1);
            int outputIndex = bytesConsumed - (bytesConsumed % 3);
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
            int remainingOut = output.Length - outputIndex;
            ReadOnlySpan<byte> tailInput = input.Slice(inputIndex, remainingOut);
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
            r = SafeDecodeFromBase64Scalar(tailInput.Slice(RemainingInputLength), output.Slice(outputIndex), out tailBytesConsumed, out tailBytesWritten, isFinalBlock, isUrl);
            int outlen = output.Slice(outputIndex).Length;

            if (r == OperationStatus.Done && paddingCharacts > 0)
            {
                // additional checks
                if ((outlen % 3 == 0) || ((outlen % 3) + 1 + paddingCharacts != 4))
                {
                    r = OperationStatus.InvalidData;
                }
            }

            bytesConsumed += tailBytesConsumed + paddingCharacts + whiteSpaces;
            bytesWritten += tailBytesWritten;
            return r;
        }




    }

}

