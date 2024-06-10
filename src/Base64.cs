using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

namespace SimdUnicode
{
    public static class Base64
    {
        // NB:their Base64 encoding function takes a byte[]
        // https://learn.microsoft.com/en-us/dotnet/api/system.convert.tobase64string?view=net-8.0#system-convert-tobase64string(system-byte())

        public enum Endianness
        {
            LITTLE = 0,
            BIG = 1
        }
        public static bool MatchSystem(Endianness e)
        {
            // Adjust the boolean comparison based on your system's architecture
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

            // Define pointers within the fixed blocks
            fixed (byte* srcInit = source)
            fixed (byte* dstInit = dest)

            {
                byte* srcEnd = srcInit + length;
                byte* src = srcInit;
                byte* dst = dstInit;

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

                        else if (idx == 3) // same story here
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
                            return OperationStatus.InvalidData;// The base64 input terminates with a single character, excluding padding.
                        }
                        bytesConsumed = (int)(src - srcInit);
                        bytesWritten = (int)(dst - dstInit);
                        return OperationStatus.Done;//SUCCESS
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

    }

}

