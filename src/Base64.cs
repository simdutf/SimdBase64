using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;


namespace SimdBase64
{
    public static class Base64
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaximalBinaryLengthFromBase64<T>(ReadOnlySpan<T> input)
        {
            return Scalar.Base64.MaximalBinaryLengthFromBase64Scalar(input);
        }
        public static byte[] FromBase64String(string s) {
            ReadOnlySpan<char> base64 = s.AsSpan();
            byte[] newBytes = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64<char>(base64)];
            int bytesConsumed = 0;
            int bytesWritten = 0;
            SimdBase64.Base64.DecodeFromBase64(base64, newBytes, out bytesConsumed, out bytesWritten, false);
            Array.Resize(ref newBytes, bytesWritten);
            return newBytes;
        }

        public unsafe static OperationStatus DecodeFromBase64(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
        {

            if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
            {
                return Arm.Base64.DecodeFromBase64ARM(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }
            // To be comleted, this may have to wait for .NET 10.
            //if (Vector512.IsHardwareAccelerated && Avx512Vbmi2.IsSupported)
            //{
            //}
            if (Avx2.IsSupported)
            {
                return AVX2.Base64.DecodeFromBase64AVX2(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }
            if (Ssse3.IsSupported && Popcnt.IsSupported)
            {
                return SSE.Base64.DecodeFromBase64SSE(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }

            return Scalar.Base64.DecodeFromBase64Scalar(source, dest, out bytesConsumed, out bytesWritten, isUrl);

        }

        public unsafe static OperationStatus DecodeFromBase64(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl = false)
        {

            if (AdvSimd.Arm64.IsSupported && BitConverter.IsLittleEndian)
            {
                return Arm.Base64.DecodeFromBase64ARM(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }
            // To be comleted, this may have to wait for .NET 10.
            //if (Vector512.IsHardwareAccelerated && Avx512Vbmi.IsSupported)
            //{
            //    return GetPointerToFirstInvalidByteAvx512(pInputBuffer, inputLength, out Utf16CodeUnitCountAdjustment, out ScalarCodeUnitCountAdjustment);
            //}
            if (Avx2.IsSupported)
            {
                return AVX2.Base64.DecodeFromBase64AVX2(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }
            if (Ssse3.IsSupported && Popcnt.IsSupported)
            {
                return SSE.Base64.DecodeFromBase64SSE(source, dest, out bytesConsumed, out bytesWritten, isUrl);
            }

            return Scalar.Base64.DecodeFromBase64Scalar(source, dest, out bytesConsumed, out bytesWritten, isUrl);

        }
    }
}
