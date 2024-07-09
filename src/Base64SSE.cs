using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO.Pipes;
using System.Text;

namespace SimdBase64
{
    public static partial class Base64
    {
        public static Vector128<byte> LookupPshufbImproved( Vector128<byte> input,bool base64Url)
        {
            // reduce  0..51 -> 0
            //        52..61 -> 1 .. 10
            //            62 -> 11
            //            63 -> 12
            Vector128<byte> result = Sse2.SubtractSaturate(input, Vector128.Create((byte)51));

            // distinguish between ranges 0..25 and 26..51:
            //         0 .. 25 -> remains 0
            //        26 .. 51 -> becomes 13
            Vector128<sbyte> less = Sse2.CompareGreaterThan(Vector128.Create((byte)26).AsSByte(), input.AsSByte());
            result = Sse2.Or(result, Sse2.And(less.AsByte(), Vector128.Create((byte)13)));

            // Create shift_LUT based on base64_url flag
            Vector128<sbyte> shiftLUT;
            if (base64Url)
            {
                shiftLUT = Vector128.Create(
                    (sbyte)('a' - 26), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52),
                    (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52),
                    (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('-' - 62),
                    (sbyte)('_' - 63), (sbyte)'A', 0, 0);
            }
            else
            {
                shiftLUT = Vector128.Create(
                    (sbyte)('a' - 26), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52),
                    (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52),
                    (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('0' - 52), (sbyte)('+' - 62),
                    (sbyte)('/' - 63), (sbyte)'A', 0, 0);
            }

            // read shift
            result = Sse42.Shuffle(shiftLUT.AsByte(), result);

            return Sse2.Add(result, input);
        }
    }

}

