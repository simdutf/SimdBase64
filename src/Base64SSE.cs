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
            ulong m0 = ToBase64Mask(base64Url, &b->chunk0, out error);
            ulong m1 = ToBase64Mask(base64Url, &b->chunk1, out error);
            ulong m2 = ToBase64Mask(base64Url, &b->chunk2, out error);
            ulong m3 = ToBase64Mask(base64Url, &b->chunk3, out error);
            return m0 | (m1 << 16) | (m2 << 32) | (m3 << 48);
        }

        public static ushort ToBase64Mask(bool base64Url, ref Vector128<byte> src, out bool error)
        {
            error = false;
                Vector128<byte> asciiSpaceTbl = Vector128.Create(
                    (byte)0x20, (byte)0x0, (byte)0x0, (byte)0x0,
                    (byte)0x0, (byte)0x0, (byte)0x0, (byte)0x0,
                    (byte)0x0, (byte)0x9, (byte)0xa, (byte)0x0,
                    (byte)0xc, (byte)0xd, (byte)0x0, (byte)0x0
                );

            // credit: aqrit
            Vector128<byte> deltaAsso;
            if (base64Url) {
                deltaAsso = Vector128.Create((byte)0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x1, 0x0, 0x0,
                                                    0x0, 0x0, 0x0, 0xF, 0x0, 0xF);
            } else {
                 deltaAsso = Vector128.Create(
                    (byte)0x1, (byte)0x1, (byte)0x1, (byte)0x1,
                    (byte)0x1, (byte)0x1, (byte)0x1, (byte)0x1,
                    (byte)0x0, (byte)0x0, (byte)0x0, (byte)0x0,
                    (byte)0x0, (byte)0xF, (byte)0x0, (byte)0xF
                );
            }

            // DEBUG : in the C++ code, Vector128.Create is 
            // __m128i _mm_setr_epi8 (char e15, char e14, char e13, char e12, char e11, char e10, char e9, char e8, char e7, char e6, char e5, char e4, char e3, char e2, char e1, char e0)
            // #include <emmintrin.h>
            // Instruction: Sequence
            // CPUID Flags: SSE2
            // Description
            // Set packed 8-bit integers in dst with the supplied values in reverse order.
            //             dst[7:0] := e15
            // dst[15:8] := e14
            // dst[23:16] := e13
            // dst[31:24] := e12
            // dst[39:32] := e11
            // dst[47:40] := e10
            // dst[55:48] := e9
            // dst[63:56] := e8
            // dst[71:64] := e7
            // dst[79:72] := e6
            // dst[87:80] := e5
            // dst[95:88] := e4
            // dst[103:96] := e3
            // dst[111:104] := e2
            // dst[119:112] := e1
            // dst[127:120] := e0
            Vector128<byte> deltaValues;
            if (base64Url)
            {
                deltaValues = Vector128.Create(
                    (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x13, // DEBUG: Potentially an error? the first row is not explicitly cast in the C++
                    (byte)0x04, (byte)0xBF, (byte)0xBF, (byte)0xB9,
                    (byte)0xB9, (byte)0x00, (byte)0x11, (byte)0xC3,
                    (byte)0xBF, (byte)0xE0, (byte)0xB9, (byte)0xB9
                );
            }
            else
            {
                deltaValues = Vector128.Create(
                    (byte)0x00, (byte)0x00, (byte)0x00, (byte)0x13,
                    (byte)0x04, (byte)0xBF, (byte)0xBF, (byte)0xB9,
                    (byte)0xB9, (byte)0x00, (byte)0x10, (byte)0xC3,
                    (byte)0xBF, (byte)0xBF, (byte)0xB9, (byte)0xB9
                );
            }
            Vector128<byte> checkAsso;
            Vector128<byte> checkValues;

            if (base64Url)
            {
                checkAsso = Vector128.Create(
                    (byte)0x0D, (byte)0x01, (byte)0x01, (byte)0x01,
                    (byte)0x01, (byte)0x01, (byte)0x01, (byte)0x01,
                    (byte)0x01, (byte)0x01, (byte)0x03, (byte)0x07,
                    (byte)0x0B, (byte)0x06, (byte)0x0B, (byte)0x12
                );

                checkValues = Vector128.Create(
                    (byte)0x00, (byte)0x80, (byte)0x80, (byte)0x80,
                    (byte)0xCF, (byte)0xBF, (byte)0xD3, (byte)0xA6,
                    (byte)0xB5, (byte)0x86, (byte)0xD0, (byte)0x80,
                    (byte)0xB0, (byte)0x80, (byte)0x00, (byte)0x00
                );
            }
            else
            {
                checkAsso = Vector128.Create(
                    (byte)0x0D, (byte)0x01, (byte)0x01, (byte)0x01,
                    (byte)0x01, (byte)0x01, (byte)0x01, (byte)0x01,
                    (byte)0x01, (byte)0x01, (byte)0x03, (byte)0x07,
                    (byte)0x0B, (byte)0x0B, (byte)0x0B, (byte)0x0F
                );

                checkValues = Vector128.Create(
                    (byte)0x80, (byte)0x80, (byte)0x80, (byte)0x80,
                    (byte)0xCF, (byte)0xBF, (byte)0xD5, (byte)0xA6,
                    (byte)0xB5, (byte)0x86, (byte)0xD1, (byte)0x80,
                    (byte)0xB1, (byte)0x80, (byte)0x91, (byte)0x80
                );
            }

            // Assuming delta_asso, delta_values, check_asso, check_values, and ascii_space_tbl are already defined
            Vector128<byte> shifted = Sse2.ShiftRightLogical(src.AsUInt16(), 3).AsByte();

            Vector128<byte> deltaHash = Sse2.Average(Ssse3.Shuffle(deltaAsso.AsByte(), src), shifted);
            Vector128<byte> checkHash = Sse2.Average(Ssse3.Shuffle(checkAsso.AsByte(), src), shifted);

            Vector128<byte> outVector = Sse2.AddSaturate(Ssse3.Shuffle(deltaValues, deltaHash), src);
            Vector128<byte> chkVector = Sse2.AddSaturate(Ssse3.Shuffle(checkValues, checkHash), src);

            int mask = Sse2.MoveMask(chkVector);
            if (mask != 0)
            {
                Vector128<byte> asciiSpace = Sse2.CompareEqual(Ssse3.Shuffle(asciiSpaceTbl, src), src);
                error |= mask != Sse2.MoveMask(asciiSpace);
            }

            src = outVector;
            return (ushort)mask;
            }
        }


        public unsafe static OperationStatus SafeDecodeFromBase64SSE(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten,  bool isUrl = false)
        {

            byte[] toBase64 = isUrl != false ? Tables.ToBase64UrlValue : Tables.ToBase64Value;



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

        byte *endOfSafe64ByteZone =
            // round up to the nearest multiple of 4, then multiplied by 3
            (bytesToProcess + 3) / 4 * 3 >= 63 ? 
                    dst + (bytesToProcess + 3) / 4 * 3 - 63 :
                    dst;

        // DEBUG:this is probalby unnescessary
        // byte* srcend = srcInit + bytesToProcess; // Assuming srclen is defined elsewhere

        const int blockSize = 6;
        Debug.Equals(blockSize >= 2, "block should of size 2 or more");
        byte[] buffer = new byte[blockSize * 64];
        // DEBUG: probably unnescessary
        // char *bufferptr = buffer; 
        if (bytesToProcess >= 64) {
            //rigth here src is at the very beginning so this is taking it
            byte* srcend64 = srcInit + bytesToProcess - 64;
            while (src <= srcend64) {
                Base64.Block64 b; //DEBUG: TODO
                Base64.LoadBlock(&b, src);//DEBUG: TODO
                src += 64;
                bool error = false;
                UInt64 badcharmask = Base64.ToBase64Mask(isUrl,&b, out error);//DEBUG: TODO
                if (error) {
                    src -= 64;
                    while (src < srcInit + bytesToProcess && toBase64[Convert.ToByte(*src)] <= 64) {
                        src++;
                    }
                    bytesConsumed = (int)(src - srcInit);
                    bytesWritten = ; // TODO
                    return OperationStatus.InvalidData;
                }
                if (badcharmask != 0) {
                    // optimization opportunity: check for simple masks like those made of
                    // continuous 1s followed by continuous 0s. And masks containing a
                    // single bad character.
                    bufferptr += compress_block(&b, badcharmask, bufferptr);
                } else if (bufferptr != buffer) {
                    copy_block(&b, bufferptr);
                    bufferptr += 64;
                } else {
                    if (dst >= end_of_safe_64byte_zone) {
                    base64_decode_block_safe(dst, &b);
                    } else {
                    base64_decode_block(dst, &b);
                    }
                    dst += 48;
                }
                if (bufferptr >= (block_size - 1) * 64 + buffer) {
                    for (size_t i = 0; i < (block_size - 2); i++) {
                    base64_decode_block(dst, buffer + i * 64);
                    dst += 48;
                    }
                    if (dst >= end_of_safe_64byte_zone) {
                    base64_decode_block_safe(dst, buffer + (block_size - 2) * 64);
                    } else {
                    base64_decode_block(dst, buffer + (block_size - 2) * 64);
                    }
                    dst += 48;
                    std::memcpy(buffer, buffer + (block_size - 1) * 64,
                                64); // 64 might be too much
                    bufferptr -= (block_size - 1) * 64;
                }
            }
        }

        char *buffer_start = buffer;
        // Optimization note: if this is almost full, then it is worth our
        // time, otherwise, we should just decode directly.
        int last_block = (int)((bufferptr - buffer_start) % 64);
        if (last_block != 0 && srcend - src + last_block >= 64) {
            while ((bufferptr - buffer_start) % 64 != 0 && src < srcend) {
            uint8_t val = to_base64[uint8_t(*src)];
            *bufferptr = char(val);
            if (val > 64) {
                return {error_code::INVALID_BASE64_CHARACTER, size_t(src - srcinit)};
            }
            bufferptr += (val <= 63);
            src++;
            }
        }

        for (; buffer_start + 64 <= bufferptr; buffer_start += 64) {
            if (dst >= end_of_safe_64byte_zone) {
            base64_decode_block_safe(dst, buffer_start);
            } else {
            base64_decode_block(dst, buffer_start);
            }
            dst += 48;
        }
        if ((bufferptr - buffer_start) % 64 != 0) {
            while (buffer_start + 4 < bufferptr) {
            uint32_t triple = ((uint32_t(uint8_t(buffer_start[0])) << 3 * 6) +
                                (uint32_t(uint8_t(buffer_start[1])) << 2 * 6) +
                                (uint32_t(uint8_t(buffer_start[2])) << 1 * 6) +
                                (uint32_t(uint8_t(buffer_start[3])) << 0 * 6))
                                << 8;
            triple = scalar::utf32::swap_bytes(triple);
            std::memcpy(dst, &triple, 4);

            dst += 3;
            buffer_start += 4;
            }
            if (buffer_start + 4 <= bufferptr) {
            uint32_t triple = ((uint32_t(uint8_t(buffer_start[0])) << 3 * 6) +
                                (uint32_t(uint8_t(buffer_start[1])) << 2 * 6) +
                                (uint32_t(uint8_t(buffer_start[2])) << 1 * 6) +
                                (uint32_t(uint8_t(buffer_start[3])) << 0 * 6))
                                << 8;
            triple = scalar::utf32::swap_bytes(triple);
            std::memcpy(dst, &triple, 3);

            dst += 3;
            buffer_start += 4;
            }
            // we may have 1, 2 or 3 bytes left and we need to decode them so let us
            // bring in src content
            int leftover = int(bufferptr - buffer_start);
            if (leftover > 0) {
            while (leftover < 4 && src < srcend) {
                uint8_t val = to_base64[uint8_t(*src)];
                if (val > 64) {
                return {error_code::INVALID_BASE64_CHARACTER, size_t(src - srcinit)};
                }
                buffer_start[leftover] = char(val);
                leftover += (val <= 63);
                src++;
            }

            if (leftover == 1) {
                return {BASE64_INPUT_REMAINDER, size_t(dst - dstinit)};
            }
            if (leftover == 2) {
                uint32_t triple = (uint32_t(buffer_start[0]) << 3 * 6) +
                                (uint32_t(buffer_start[1]) << 2 * 6);
                triple = scalar::utf32::swap_bytes(triple);
                triple >>= 8;
                std::memcpy(dst, &triple, 1);
                dst += 1;
            } else if (leftover == 3) {
                uint32_t triple = (uint32_t(buffer_start[0]) << 3 * 6) +
                                (uint32_t(buffer_start[1]) << 2 * 6) +
                                (uint32_t(buffer_start[2]) << 1 * 6);
                triple = scalar::utf32::swap_bytes(triple);

                triple >>= 8;

                std::memcpy(dst, &triple, 2);
                dst += 2;
            } else {
                uint32_t triple = ((uint32_t(uint8_t(buffer_start[0])) << 3 * 6) +
                                (uint32_t(uint8_t(buffer_start[1])) << 2 * 6) +
                                (uint32_t(uint8_t(buffer_start[2])) << 1 * 6) +
                                (uint32_t(uint8_t(buffer_start[3])) << 0 * 6))
                                << 8;
                triple = scalar::utf32::swap_bytes(triple);
                std::memcpy(dst, &triple, 3);
                dst += 3;
            }
            }
        }
        if (src < srcend + equalsigns) {
            result r =
                scalar::base64::base64_tail_decode(dst, src, srcend - src, options);
            if (r.error == error_code::INVALID_BASE64_CHARACTER) {
            r.count += size_t(src - srcinit);
            return r;
            } else {
            r.count += size_t(dst - dstinit);
            }
            if(r.error == error_code::SUCCESS && equalsigns > 0) {
            // additional checks
            if((r.count % 3 == 0) || ((r.count % 3) + 1 + equalsigns != 4)) {
                r.error = error_code::INVALID_BASE64_CHARACTER;
            }
            }
            return r;
        }
        if(equalsigns > 0) {
            if((size_t(dst - dstinit) % 3 == 0) || ((size_t(dst - dstinit) % 3) + 1 + equalsigns != 4)) {
            return {INVALID_BASE64_CHARACTER, size_t(dst - dstinit)};
            }
        }
        return {SUCCESS, size_t(dst - dstinit)};
        }

    }

}

