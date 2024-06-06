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

        public enum ErrorCode
        {
            SUCCESS = 0,
            INVALID_BASE64_CHARACTER, // Found a character that cannot be part of a valid base64 string.
            BASE64_INPUT_REMAINDER, // The base64 input terminates with a single character, excluding padding.
            OUTPUT_BUFFER_TOO_SMALL, // The provided buffer is too small for the output.
            OTHER // Not related to validation/transcoding.
        }

        public struct Result
        {
            public ErrorCode Error { get; set; }
            public long Count { get; set; } // Use 'int' instead of 'size_t'

            // Default constructor
            public Result() : this(ErrorCode.SUCCESS) { }

            // Constructor with parameters
            public Result(ErrorCode error)
            {
                Error = error;
            }

            // TODO:Fix this when it becomes needed
            public override bool Equals(object obj)
            {
                throw new NotImplementedException();
            }

            public override int GetHashCode()
            {
                throw new NotImplementedException();
            }

            public static bool operator ==(Result left, Result right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Result left, Result right)
            {
                return !(left == right);
            }
        }



        // public unsafe static Result Base64TailDecode(byte* dst, byte* src, int length, Base64Options options)
            public unsafe static OperationStatus DecodeFromBase64(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true, bool isUrl = false)
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

                        // Todo:make all this conform to the API eg probably no error code
                        // return new ErrorCode.INVALID_BASE64_CHARACTER;// Found a character that cannot be part of a valid base64 string.
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
                            dst[0] = byteTriple[0];  // Copy only the first byte
                            // TODO/check:
                            // In the C++ code, there is this :
                            // std::memcpy(dst, &triple, 1);
                            // but I am not sure if its worth it for only 1 byte? 
                            // Intuitively It sounds like something the compiler can take care of in this context?
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
                            // std::memcpy(dst, &triple, 2);
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
                        // return new Result(ErrorCode.BASE64_INPUT_REMAINDER);
                        return OperationStatus.Done;// The base64 input terminates with a single character, excluding padding.
                    }
                    bytesConsumed = (int)(src - srcInit);
                    bytesWritten = (int)(dst - dstInit);
                    return OperationStatus.Done;//SUCCESS
                }
                triple =
                    ((uint)(buffer[0]) << 3 * 6) + ((uint)(buffer[1]) << 2 * 6) +
                    ((uint)(buffer[2]) << 1 * 6) + ((uint)(buffer[3]) << 0 * 6);
                if(MatchSystem(Endianness.BIG)) {
                    triple <<= 8;
                    // std::memcpy(dst, &triple, 3);
                    Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);

                } else {
                triple = SwapBytes(triple);
                triple >>= 8;
                // std::memcpy(dst, &triple, 3);
                    Marshal.Copy(BitConverter.GetBytes(triple), 0, (IntPtr)dst, 3);
                }
                dst += 3;
            }

            }





        }

    }

}

