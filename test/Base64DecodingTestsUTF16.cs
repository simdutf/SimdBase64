namespace tests;
using System.Text;
using SimdBase64;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Buffers;
using Newtonsoft.Json;

public partial class Base64DecodingTests{

    public delegate OperationStatus DecodeFromBase64DelegateFncFromUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
    public delegate OperationStatus DecodeFromBase64DelegateSafeFomUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
    public delegate OperationStatus Base64WithWhiteSpaceToBinaryFromUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);


    protected static void DecodeBase64CasesUTF16(DecodeFromBase64DelegateFncFromUTF16 DecodeFromBase64Delegate)
    {
        var cases = new List<char[]> { new char[] { (char)0x53, (char)0x53 } };
        // Define expected results for each case
        var expectedResults = new List<(OperationStatus, int)> { (OperationStatus.Done, 1) };

        for (int i = 0; i < cases.Count; i++)
        {
            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(cases[i].AsSpan())];
            int bytesConsumed;
            int bytesWritten;

            var result = DecodeFromBase64Delegate(cases[i], buffer, out bytesConsumed, out bytesWritten, false);

            Assert.Equal(expectedResults[i].Item1, result);
            Assert.Equal(expectedResults[i].Item2, bytesWritten);
        }
    }



    [Fact]
    [Trait("Category", "scalar")]
    public void DecodeBase64CasesScalarTUF16()
    {
        DecodeBase64CasesUTF16(Base64.DecodeFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "SSE")]
    public void DecodeBase64CasesSSETUF16()
    {
        DecodeBase64CasesUTF16(Base64.DecodeFromBase64SSE);
    }

    protected static void CompleteDecodeBase64CasesUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("abcd", " Y\fW\tJ\njZ A=\r= "),
    };

        foreach (var (decoded, base64) in cases)
        {
            // byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);
            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }

        foreach (var (decoded, base64) in cases)
        {
            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = DecodeFromBase64DelegateSafeFomUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);

            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }


        }

    }

    [Fact]
    [Trait("Category", "scalar")]
    public void CompleteDecodeBase64CasesScalarUTF16()
    {
        CompleteDecodeBase64CasesUTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void CompleteDecodeBase64CasesSSEUTF16()
    {
        CompleteDecodeBase64CasesUTF16(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected static void MoreDecodeTestsUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("Hello, World!", "SGVsbG8sIFdvcmxkIQ=="),
        ("GeeksforGeeks", "R2Vla3Nmb3JHZWVrcw=="),
        ("123456", "MTIzNDU2"),
        ("Base64 Encoding", "QmFzZTY0IEVuY29kaW5n"),
        ("!R~J2jL&mI]O)3=c:G3Mo)oqmJdxoprTZDyxEvU0MI.'Ww5H{G>}y;;+B8E_Ah,Ed[ PdBqY'^N>O$4:7LK1<:|7)btV@|{YWR$$Er59-XjVrFl4L}~yzTEd4'E[@k", "IVJ+SjJqTCZtSV1PKTM9YzpHM01vKW9xbUpkeG9wclRaRHl4RXZVME1JLidXdzVIe0c+fXk7OytCOEVfQWgsRWRbIFBkQnFZJ15OPk8kNDo3TEsxPDp8NylidFZAfHtZV1IkJEVyNTktWGpWckZsNEx9fnl6VEVkNCdFW0Br")
    };

        foreach (var (decoded, base64) in cases)
        {
            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
            Assert.Equal(OperationStatus.Done, result);
            Assert.True(OperationStatus.Done == result, $"Decoding string {decoded} with Length {decoded.Length} bytes went wrong");
            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.True(decoded[i] == (char)buffer[i], $"Decoded character not equal to source at location {i}: \n Actual: {(char)buffer[i]} ,\n Expected: {decoded[i]},\n Actual string: {BitConverter.ToString(buffer)},\n Expected string :{decoded} ");
            }
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);

        }

        foreach (var (decoded, base64) in cases)
        {

            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = DecodeFromBase64DelegateSafeFomUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);

            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }

    }

    [Fact]
    [Trait("Category", "scalar")]
    public void MoreDecodeTestsScalarUTF16()
    {
        MoreDecodeTestsUTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [Fact]
    [Trait("Category", "SSE")]
    public void MoreDecodeTestsSSEUTF16()
    {
        MoreDecodeTestsUTF16(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void MoreDecodeTestsUrlUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("Hello, World!", "SGVsbG8sIFdvcmxkIQ=="),
        ("GeeksforGeeks", "R2Vla3Nmb3JHZWVrcw=="),
        ("123456", "MTIzNDU2"),
        ("Base64 Encoding", "QmFzZTY0IEVuY29kaW5n"),
        ("!R~J2jL&mI]O)3=c:G3Mo)oqmJdxoprTZDyxEvU0MI.'Ww5H{G>}y;;+B8E_Ah,Ed[ PdBqY'^N>O$4:7LK1<:|7)btV@|{YWR$$Er59-XjVrFl4L}~yzTEd4'E[@k", "IVJ-SjJqTCZtSV1PKTM9YzpHM01vKW9xbUpkeG9wclRaRHl4RXZVME1JLidXdzVIe0c-fXk7OytCOEVfQWgsRWRbIFBkQnFZJ15OPk8kNDo3TEsxPDp8NylidFZAfHtZV1IkJEVyNTktWGpWckZsNEx9fnl6VEVkNCdFW0Br")
    };

        foreach (var (decoded, base64) in cases)
        {


            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);
            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }

        foreach (var (decoded, base64) in cases)
        {

            ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64.ToCharArray());
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = DecodeFromBase64DelegateSafeFomUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);

            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }
    }

    [Fact]
    [Trait("Category", "sse")]
    public void MoreDecodeTestsUrlUTF16SSE()
    {
        MoreDecodeTestsUrlUTF16(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void MoreDecodeTestsUTF16UrlUTF16Scalar()
    {
        MoreDecodeTestsUrlUTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void RoundtripBase64UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source);

            byte[] decodedBytes = new byte[len];

            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                base64String.ToCharArray(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: false);

            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
            Assert.True(len == bytesWritten, $" Expected bytesWritten: {len} , Actual: {bytesWritten}");
            Assert.True(base64String.Length == bytesConsumed, $" Expected bytesConsumed: {base64String.Length} , Actual: {bytesConsumed}");
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64ScalarUTF16()
    {
        RoundtripBase64UTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void RoundtripBase64SSEUtf16()
    {
        RoundtripBase64UTF16(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void RoundtripBase64UrlUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source).Replace('+', '-').Replace('/', '_'); ;

            byte[] decodedBytes = new byte[len];

            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                base64String.ToCharArray(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: true);

            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64UrlScalarUTF16()
    {
        RoundtripBase64UrlUTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void RoundtripBase64UrlSSEUtf16()
    {
        RoundtripBase64UrlUTF16(Base64.DecodeFromBase64SSE, Base64.DecodeFromBase64SSE);
    }

    protected static void BadPaddingBase64UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        Random random = new Random(1234); // use deterministic seed for reproducibility

        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
            int bytesConsumed;
            int bytesWritten;

            for (int trial = 0; trial < 10; trial++)
            {
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                string base64 = Convert.ToBase64String(source); // Encode source bytes to Base64
                int padding = base64.EndsWith('=') ? 1 : 0;
                padding += base64.EndsWith("==", StringComparison.InvariantCulture) ? 1 : 0;

                if (padding != 0)
                {
                    try
                    {

                        // Test adding padding characters should break decoding
                        List<char> modifiedBase64 = (base64 + "=").ToList();
                        byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
                        for (int i = 0; i < 5; i++)
                        {
                            AddSpace(modifiedBase64.ToList(), random);
                        }

                        var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                            modifiedBase64.ToArray(), buffer.AsSpan(),
                            out bytesConsumed, out bytesWritten, isUrl: false);

                        Assert.Equal(OperationStatus.InvalidData, result);
                    }
                    catch (FormatException)
                    {
                        if (padding == 2)
                        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                            Console.WriteLine($"Wrong OperationStatus when adding one padding character to TWO padding character");
                        }
                        else if (padding == 1)
                        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                            Console.WriteLine($"Wrong OperationStatus when adding one padding character to ONE padding character");
                        }
                    }

                    if (padding == 2)
                    {
                        try
                        {

                            // removing one padding characters should break decoding
                            List<char> modifiedBase64 = base64.Substring(0, base64.Length - 1).ToList();
                            byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
                            for (int i = 0; i < 5; i++)
                            {
                                AddSpace(modifiedBase64.ToList(), random);
                            }

                            var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                                modifiedBase64.ToArray(), buffer.AsSpan(),
                                out bytesConsumed, out bytesWritten, isUrl: false);

                            Assert.Equal(OperationStatus.InvalidData, result);
                        }
                        catch (FormatException)
                        {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                            Console.WriteLine($"Wrong OperationStatus when substracting one padding character");
                        }
                    }
                }
                else
                {
                    try
                    {

                        // Test adding padding characters should break decoding
                        List<char> modifiedBase64 = (base64 + "=").ToList();
                        byte[] buffer = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
                        for (int i = 0; i < 5; i++)
                        {
                            AddSpace(modifiedBase64.ToList(), random);
                        }

                        var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                            modifiedBase64.ToArray(), buffer.AsSpan(),
                            out bytesConsumed, out bytesWritten, isUrl: false);

                        Assert.Equal(OperationStatus.InvalidData, result);
                    }
                    catch (FormatException)
                    {
#pragma warning disable CA1303 // Do not pass literals as localized parameters
                        Console.WriteLine($"Wrong OperationStatus when adding one padding character to base64 string with no padding charater");
                    }
                }

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void BadPaddingUTF16Base64Scalar()
    {
        BadPaddingBase64UTF16(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void BadPaddingUTF16Base64SSE()
    {
        BadPaddingBase64UTF16(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


//     protected void DoomedBase64Roundtrip(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }
//         for (int len = 0; len < 2048; len++)
//         {
//             byte[] source = new byte[len];

//             for (int trial = 0; trial < 10; trial++)
//             {
//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;
// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Generate random bytes for source

//                 byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));

//                 (byte[] base64WithGarbage, int location) = AddGarbage(base64, random);

//                 // Prepare a buffer for decoding the base64 back to binary
//                 byte[] back = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];

//                 // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
//                 var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                     base64WithGarbage.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
//                 Assert.Equal(location, bytesConsumed);
//                 Assert.Equal(location / 4 * 3, bytesWritten);

//                 // Also test safe decoding with a specified back_length
//                 var safeResult = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64WithGarbage.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.InvalidData, safeResult);
//                 Assert.Equal(location, bytesConsumed);
//                 Assert.Equal(location / 4 * 3, bytesWritten);

//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void DoomedBase64RoundtripScalar()
//     {
//         DoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void DoomedBase64RoundtripSSE()
//     {
//         DoomedBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }



//     protected void TruncatedDoomedBase64Roundtrip(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }
//         for (int len = 1; len < 2048; len++)
//         {
//             byte[] source = new byte[len];

//             for (int trial = 0; trial < 10; trial++)
//             {

//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;
// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Generate random bytes for source

//                 byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));

//                 byte[] base64Truncated = base64[..^3];  // removing last 3 elements with a view

//                 // Prepare a buffer for decoding the base64 back to binary
//                 byte[] back = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Truncated)];

//                 // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
//                 var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                     base64Truncated.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.NeedMoreData, result);
//                 Assert.Equal((base64.Length - 4) / 4 * 3, bytesWritten);
//                 Assert.Equal(base64Truncated.Length, bytesConsumed);

//                 var safeResult = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64Truncated.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.NeedMoreData, safeResult);
//                 Assert.Equal((base64.Length - 4) / 4 * 3, bytesWritten);
//                 Assert.Equal(base64Truncated.Length, bytesConsumed);

//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void TruncatedDoomedBase64RoundtripScalar()
//     {
//         TruncatedDoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void TruncatedDoomedBase64RoundtripSSE()
//     {
//         TruncatedDoomedBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected void RoundtripBase64WithSpaces(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }
//         for (int len = 0; len < 2048; len++)
//         {
//             // Initialize source buffer with random bytes
//             byte[] source = new byte[len];
// #pragma warning disable CA5394 // Do not use insecure randomness
//             random.NextBytes(source);

//             // Encode source to Base64
//             string base64String = Convert.ToBase64String(source);
//             byte[] base64 = Encoding.UTF8.GetBytes(base64String);

//             for (int i = 0; i < 5; i++)
//             {
//                 AddSpace(base64.ToList(), random);
//             }


//             // Prepare buffer for decoded bytes
//             byte[] decodedBytes = new byte[len];

//             // Call your custom decoding function
//             int bytesConsumed, bytesWritten;
//             var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                 base64.AsSpan(), decodedBytes.AsSpan(),
//                 out bytesConsumed, out bytesWritten, isUrl: false);

//             // Assert that decoding was successful
//             Assert.Equal(OperationStatus.Done, result);
//             Assert.Equal(len, bytesWritten);
//             Assert.Equal(base64String.Length, bytesConsumed);
//             Assert.Equal(source, decodedBytes.AsSpan().ToArray());

//             // Safe version not working
//             result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                base64.AsSpan(), decodedBytes.AsSpan(),
//                out bytesConsumed, out bytesWritten, isUrl: false);

//             // Assert that decoding was successful
//             Assert.Equal(OperationStatus.Done, result);
//             Assert.Equal(len, bytesWritten);
//             Assert.Equal(base64String.Length, bytesConsumed);
//             Assert.Equal(source, decodedBytes.AsSpan().ToArray());
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void RoundtripBase64WithSpacesScalar()
//     {
//         RoundtripBase64WithSpaces(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void RoundtripBase64WithSpacesSSE()
//     {
//         RoundtripBase64WithSpaces(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected void AbortedSafeRoundtripBase64(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }
//         for (int offset = 1; offset <= 16; offset += 3)
//         {
//             for (int len = offset; len < 1024; len++)
//             {
//                 byte[] source = new byte[len];
// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Initialize source buffer with random bytes

//                 string base64String = Convert.ToBase64String(source);

//                 byte[] base64 = Encoding.UTF8.GetBytes(base64String);



//                 int limitedLength = len - offset; // intentionally too little
//                 byte[] tooSmallArray = new byte[limitedLength];

//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;

//                 var result = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64.AsSpan(), tooSmallArray.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.DestinationTooSmall, result);
//                 Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());



//                 // Now let us decode the rest !!!
//                 ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

//                 byte[] decodedRemains = new byte[len - bytesWritten];

//                 int remainingBytesConsumed = 0;
//                 int remainingBytesWritten = 0;

//                 result = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64Remains, decodedRemains.AsSpan(),
//                     out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

//                 Assert.Equal(OperationStatus.Done, result);
//                 Assert.Equal(len, bytesWritten + remainingBytesWritten);
//                 Assert.Equal(source.Skip(bytesWritten).ToArray(), decodedRemains.ToArray());
//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void AbortedSafeRoundtripBase64Scalar()
//     {
//         AbortedSafeRoundtripBase64(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void AbortedSafeRoundtripBase64SSE()
//     {
//         AbortedSafeRoundtripBase64(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected void AbortedSafeRoundtripBase64WithSpaces(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }
//         for (int offset = 1; offset <= 16; offset += 3)
//         {
//             for (int len = offset; len < 1024; len++)
//             {
//                 byte[] source = new byte[len];
// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Initialize source buffer with random bytes

//                 string base64String = Convert.ToBase64String(source);

//                 byte[] base64 = Encoding.UTF8.GetBytes(base64String);
//                 for (int i = 0; i < 5; i++)
//                 {
//                     AddSpace(base64.ToList(), random);
//                 }

//                 int limitedLength = len - offset; // intentionally too little
//                 byte[] tooSmallArray = new byte[limitedLength];

//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;

//                 var result = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64.AsSpan(), tooSmallArray.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.DestinationTooSmall, result);
//                 Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());

//                 // Now let us decode the rest !!!
//                 ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

//                 byte[] decodedRemains = new byte[len - bytesWritten];

//                 int remainingBytesConsumed = 0;
//                 int remainingBytesWritten = 0;

//                 result = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64Remains, decodedRemains.AsSpan(),
//                     out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

//                 Assert.Equal(OperationStatus.Done, result);
//                 Assert.Equal(len, bytesWritten + remainingBytesWritten);
//                 Assert.Equal(source.Skip(bytesWritten).ToArray(), decodedRemains.ToArray());
//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void AbortedSafeRoundtripBase64WithSpacesScalar()
//     {
//         AbortedSafeRoundtripBase64WithSpaces(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }


//     [Fact]
//     [Trait("Category", "sse")]
//     public void AbortedSafeRoundtripBase64WithSpacesSSE()
//     {
//         AbortedSafeRoundtripBase64WithSpaces(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected void StreamingBase64Roundtrip(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         int len = 2048;
//         byte[] source = new byte[len];
// #pragma warning disable CA5394 // Do not use insecure randomness
//         random.NextBytes(source); // Initialize source buffer with random bytes

//         string base64String = Convert.ToBase64String(source);

//         byte[] base64 = Encoding.UTF8.GetBytes(base64String);

//         for (int window = 16; window <= 2048; window += 7)
//         {
//             // build a buffer with enough space to receive the decoded base64
//             int bytesConsumed = 0;
//             int bytesWritten = 0;

//             byte[] decodedBytes = new byte[len];
//             int outpos = 0;
//             for (int pos = 0; pos < base64.Length; pos += window)
//             {
//                 int windowsBytes = Math.Min(window, base64.Length - pos);

// #pragma warning disable CA1062
//                 var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                     base64.AsSpan().Slice(pos, windowsBytes), decodedBytes.AsSpan().Slice(outpos),
//                     out bytesConsumed, out bytesWritten, isUrl: false);

//                 Assert.True(result != OperationStatus.InvalidData);

//                 if (windowsBytes + pos == base64.Length)
//                 {

//                     // We must check that the last call to base64_to_binary did not
//                     // end with an OperationStatus.NeedMoreData error.
//                     Assert.Equal(OperationStatus.Done, result);
//                 }
//                 else
//                 {
//                     int tailBytesToReprocess = 0;
//                     if (result == OperationStatus.NeedMoreData)
//                     {
//                         tailBytesToReprocess = 1;
//                     }
//                     else
//                     {
//                         tailBytesToReprocess = (bytesWritten % 3) == 0 ? 0 : (bytesWritten % 3) + 1;
//                     }
//                     pos -= tailBytesToReprocess;
//                     bytesWritten -= bytesWritten % 3;
//                 }
//                 outpos += bytesWritten;
//             }
//             Assert.Equal(source, decodedBytes);
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void StreamingBase64RoundtripScalar()
//     {
//         StreamingBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }


//     [Fact]
//     [Trait("Category", "sse")]
//     public void StreamingBase64RoundtripSSE()
//     {
//         StreamingBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected static void ReadmeTest(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         int len = 2048;
//         string source = new string('a', len);
//         byte[] base64 = Encoding.UTF8.GetBytes(source);

//         // Calculate the required size for 'decoded' to accommodate Base64 decoding
//         byte[] decodedBytes = new byte[(len + 3) / 4 * 3];
//         int outpos = 0;
//         int window = 512;

//         for (int pos = 0; pos < base64.Length; pos += window)
//         {
//             int bytesConsumed = 0;
//             int bytesWritten = 0;

//             // how many base64 characters we can process in this iteration
//             int windowsBytes = Math.Min(window, base64.Length - pos);
// #pragma warning disable CA1062 //validate parameter 'Base64WithWhiteSpaceToBinaryFromUTF16' is non-null before using it.
//             var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                 base64.AsSpan().Slice(pos, windowsBytes), decodedBytes.AsSpan().Slice(outpos),
//                 out bytesConsumed, out bytesWritten, isUrl: false);

//             Assert.True(result != OperationStatus.InvalidData, $"Invalid base64 character at position {pos + bytesConsumed}");

//             // If we arrived at the end of the base64 input, we must check that the
//             // number of characters processed is a multiple of 4, or that we have a
//             // remainder of 0, 2 or 3.                    
//             // Eg we must check that the last call to base64_to_binary did not
//             // end with an OperationStatus.NeedMoreData error.

//             if (windowsBytes + pos == base64.Length)
//             {
//                 Assert.Equal(OperationStatus.Done, result);
//             }
//             else
//             {
//                 // If we are not at the end, we may have to reprocess either 1, 2 or 3
//                 // bytes, and to drop the last 0, 2 or 3 bytes decoded.
//                 int tailBytesToReprocess = 0;
//                 if (result == OperationStatus.NeedMoreData)
//                 {
//                     tailBytesToReprocess = 1;
//                 }
//                 else
//                 {
//                     tailBytesToReprocess = (bytesWritten % 3) == 0 ? 0 : (bytesWritten % 3) + 1;
//                 }
//                 pos -= tailBytesToReprocess;
//                 bytesWritten -= bytesWritten % 3;
//                 outpos += bytesWritten;
//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void ReadmeTestScalar()
//     {
//         ReadmeTest(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }


//     [Fact]
//     [Trait("Category", "sse")]
//     public void ReadmeTestSSE()
//     {
//         ReadmeTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected static void ReadmeTestSafe(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         int len = 72;
//         string source = new string('a', len);
//         byte[] base64 = Encoding.UTF8.GetBytes(source);

//         byte[] decodedBytesTooSmall = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64) / 2]; // Intentionally too small

//         int bytesConsumed = 0;
//         int bytesWritten = 0;

//         var result = DecodeFromBase64DelegateSafeFomUTF16(
//             base64.AsSpan(), decodedBytesTooSmall.AsSpan(),
//             out bytesConsumed, out bytesWritten, isUrl: false);
//         Assert.Equal(OperationStatus.DestinationTooSmall, result);

//         // We decoded 'limited_length' bytes to back.
//         // Now let us decode the rest !!!        
//         byte[] decodedRemains = new byte[len - bytesWritten];
//         ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

//         int remainingBytesConsumed = 0;
//         int remainingBytesWritten = 0;

//         result = DecodeFromBase64DelegateSafeFomUTF16(
//             base64Remains, decodedRemains.AsSpan(),
//             out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

//         Assert.Equal(OperationStatus.Done, result);
//         Assert.Equal(base64.Length, remainingBytesConsumed + bytesConsumed);
//         Assert.Equal(Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64), remainingBytesWritten + bytesWritten);
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void ReadmeTestSafeScalar()
//     {
//         ReadmeTestSafe(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }


//     [Fact]
//     [Trait("Category", "sse")]
//     public void ReadmeTestSafeSSE()
//     {
//         ReadmeTestSafe(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected void DoomedBase64AtPos0(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFomUTF16 == null || Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
//         {
// #pragma warning disable CA2208
//             throw new ArgumentNullException("Unexpected null parameter");
//         }

//         List<int> positions = new List<int>();
//         for (int i = 0; i < Tables.ToBase64Value.Length; i++)
//         {
//             if (Tables.ToBase64Value[i] == 255)
//             {
//                 positions.Add(i);
//             }
//         }
//         for (int len = 57; len < 2048; len++)
//         {
//             byte[] source = new byte[len];

//             for (int i = 0; i < positions.Count; i++)
//             {
//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;
// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Generate random bytes for source

//                 byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));



//                 (byte[] base64WithGarbage, int location) = AddGarbage(base64, random, 0);

//                 // Prepare a buffer for decoding the base64 back to binary
//                 byte[] back = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];

//                 // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
//                 var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                     base64WithGarbage.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.InvalidData, result);
//                 Assert.Equal(location, bytesConsumed);
//                 Assert.Equal(location / 4 * 3, bytesWritten);

//                 // Also test safe decoding with a specified back_length
//                 var safeResult = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64WithGarbage.AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.Equal(OperationStatus.InvalidData, safeResult);
//                 Assert.Equal(location, bytesConsumed);
//                 Assert.Equal(location / 4 * 3, bytesWritten);

//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void DoomedBase64AtPos0Scalar()
//     {
//         DoomedBase64AtPos0(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void DoomedBase64AtPos0SSE()
//     {
//         DoomedBase64AtPos0(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     protected static void EnronFilesTest(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         string[] fileNames = Directory.GetFiles("../../../../benchmark/data/email");
//         string[] FileContent = new string[fileNames.Length];

//         for (int i = 0; i < fileNames.Length; i++)
//         {
//             FileContent[i] = File.ReadAllText(fileNames[i]);
//         }

//         foreach (string s in FileContent)
//         {
//             byte[] base64 = Encoding.UTF8.GetBytes(s);

//             Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64)];
//             int bytesConsumed = 0;
//             int bytesWritten = 0;

//             var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64.AsSpan(), output, out bytesConsumed, out bytesWritten, false);

//             int bytesConsumedScalar = 0;
//             int bytesWrittenScalar = 0;

//             var resultScalar = DecodeFromBase64DelegateSafeFomUTF16(base64.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

//             Assert.True(result == resultScalar);
//             Assert.True(result == OperationStatus.Done);
//             Assert.True(bytesConsumed== bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
//             Assert.True(bytesWritten== bytesWrittenScalar);
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void EnronFilesTestScalar()
//     {
//         EnronFilesTest(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void EnronFilesTestSSE()
//     {
//         EnronFilesTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }


//     protected static void SwedenZoneBaseFileTest(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//         string FilePath = "../../../../benchmark/data/dns/swedenzonebase.txt";
//         // Read the contents of the file
//         string fileContent = File.ReadAllText(FilePath);

//         // Convert file content to byte array (assuming it's base64 encoded)
//         byte[] base64Bytes = Encoding.UTF8.GetBytes(fileContent);

//         Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64Bytes)];


//         // Decode the base64 content
//         int bytesConsumed, bytesWritten;
//         var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64Bytes, output, out bytesConsumed, out bytesWritten, false);

//         // Assert that the decoding was successful

//         int bytesConsumedScalar = 0;
//         int bytesWrittenScalar = 0;

//         var resultScalar = DecodeFromBase64DelegateSafeFomUTF16(base64Bytes.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

//         Assert.True( result == resultScalar,"result != resultScalar");
//         Assert.True(bytesConsumed== bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
//         Assert.True(bytesWritten == bytesWrittenScalar, $"bytesWritten: {bytesWritten},bytesWrittenScalar:{bytesWrittenScalar}");
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void SwedenZoneBaseFileTestScalar()
//     {
//         SwedenZoneBaseFileTest(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void SwedenZoneBaseFileTestSSE()
//     {
//         SwedenZoneBaseFileTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }



//     protected void DoomedPartialBuffer(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFomUTF16 DecodeFromBase64DelegateSafeFomUTF16, Base64.MaximalBinaryLengthFromBase64Scalar<char>Fnc Base64.MaximalBinaryLengthFromBase64Scalar<char>)
//     {
//          byte[] VectorToBeCompressed = new byte[] {
//         0x6D, 0x6A, 0x6D, 0x73, 0x41, 0x71, 0x39, 0x75,
//         0x76, 0x6C, 0x77, 0x48, 0x20, 0x77, 0x33, 0x53
//     };
        
//         for (int len = 0; len < 2048; len++)
//         {
//             byte[] source = new byte[len];

//             for (int trial = 0; trial < 10; trial++)
//             {
//                 int bytesConsumed = 0;
//                 int bytesWritten = 0;

//                 int bytesConsumedSafe = 0;
//                 int bytesWrittenSafe = 0;

// #pragma warning disable CA5394 // Do not use insecure randomness
//                 random.NextBytes(source); // Generate random bytes for source

//                 byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));


//                 (byte[] base64WithGarbage, int location) = AddGarbage(base64, random);

//                 // Insert 1 to 5 copies of the vector right before the garbage
//                 int numberOfCopies = random.Next(1, 6); // Randomly choose 1 to 5 copies
//                 List<byte> base64WithGarbageAndTrigger = new List<byte>(base64WithGarbage);
//                 int insertPosition = location; // Insert right before the garbage

//                 for (int i = 0; i < numberOfCopies; i++)
//                 {
//                     base64WithGarbageAndTrigger.InsertRange(insertPosition, VectorToBeCompressed);
//                     insertPosition += VectorToBeCompressed.Length;
//                 }

//                 // Update the location to reflect the new position of the garbage byte
//                 location += insertPosition;

//                 // Prepare a buffer for decoding the base64 back to binary
//                 byte[] back = new byte[Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64WithGarbageAndTrigger.ToArray())];

//                 // Attempt to decode base64 back to binary and assert that it fails
//                 var result = Base64WithWhiteSpaceToBinaryFromUTF16(
//                     base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
//                     out bytesConsumed, out bytesWritten, isUrl: false);
//                 Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
//                 Assert.Equal(insertPosition, bytesConsumed);

//                 // Also test safe decoding with a specified back_length
//                 var safeResult = DecodeFromBase64DelegateSafeFomUTF16(
//                     base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
//                     out bytesConsumedSafe, out bytesWrittenSafe, isUrl: false);

//                 Assert.True(result == safeResult);
//                 Assert.True(bytesConsumedSafe == bytesConsumed, $"bytesConsumedSafe :{bytesConsumedSafe} != bytesConsumed {bytesConsumed}");
//                 Assert.True(bytesWrittenSafe == bytesWritten,$"bytesWrittenSafe :{bytesWrittenSafe} != bytesWritten {bytesWritten}");

//             }
//         }
//     }

//     [Fact]
//     [Trait("Category", "scalar")]
//     public void DoomedPartialBufferScalar()
//     {
//         DoomedPartialBuffer(Base64.Base64WithWhiteSpaceToBinaryFromUTF16Scalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }

//     [Fact]
//     [Trait("Category", "sse")]
//     public void DoomedPartialBufferSSE()
//     {
//         DoomedPartialBuffer(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
//     }





}








