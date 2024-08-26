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

#pragma warning disable CA1515
public partial class Base64DecodingTests{
#pragma warning disable CA1515
    public delegate OperationStatus DecodeFromBase64DelegateFncFromUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
#pragma warning disable CA1515
    public delegate OperationStatus DecodeFromBase64DelegateSafeFromUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
#pragma warning disable CA1515
    public delegate OperationStatus Base64WithWhiteSpaceToBinaryFromUTF16(ReadOnlySpan<char> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);


    protected static void DecodeBase64CasesUTF16(DecodeFromBase64DelegateFncFromUTF16 DecodeFromBase64Delegate)
    {
        var cases = new List<char[]> { new char[] { (char)0x53, (char)0x53 } };
        // Define expected results for each case
        var expectedResults = new List<(OperationStatus, int)> { (OperationStatus.Done, 1) };

        for (int i = 0; i < cases.Count; i++)
        {
            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(cases[i].AsSpan())];
            int bytesConsumed;
            int bytesWritten;
#pragma warning disable CA1062
            var result = DecodeFromBase64Delegate(cases[i], buffer, out bytesConsumed, out bytesWritten, false);

            Assert.Equal(expectedResults[i].Item1, result);
            Assert.Equal(expectedResults[i].Item2, bytesWritten);
        }
    }

    [Fact]
    public void DecodeBase64Readme()
    {
        string base64 = "SGVsbG8sIFdvcmxkIQ==";
        byte[] buffer = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64(base64.AsSpan())];
        int bytesConsumed;
        int bytesWritten;
        var result = SimdBase64.Base64.DecodeFromBase64(base64.AsSpan(), buffer, out bytesConsumed, out bytesWritten, false);
        Assert.Equal(OperationStatus.Done, result);
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(buffer.AsSpan().Slice(0, bytesWritten)));
    }


    [Fact]
    [Trait("Category", "scalar")]
    public void DecodeBase64CasesScalarTUF16()
    {
        DecodeBase64CasesUTF16(SimdBase64.Scalar.Base64.DecodeFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void DecodeBase64CasesSSETUF16()
    {
        DecodeBase64CasesUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE);
    }

    protected static void CompleteDecodeBase64CasesUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
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
            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
#pragma warning disable CA1062
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
            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
#pragma warning disable CA1062
            var result = DecodeFromBase64DelegateSafeFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
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
        CompleteDecodeBase64CasesUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void CompleteDecodeBase64CasesSSEUTF16()
    {
        CompleteDecodeBase64CasesUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected static void MoreDecodeTestsUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
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

            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
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

            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = DecodeFromBase64DelegateSafeFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
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
        MoreDecodeTestsUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void MoreDecodeTestsSSEUTF16()
    {
        MoreDecodeTestsUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void MoreDecodeTestsUrlUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
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

            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
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

            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Span)];
            var result = DecodeFromBase64DelegateSafeFromUTF16(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);

            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void MoreDecodeTestsUrlUTF16SSE()
    {
        MoreDecodeTestsUrlUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void MoreDecodeTestsUTF16UrlUTF16Scalar()
    {
        MoreDecodeTestsUrlUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void RoundtripBase64UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source);

            byte[] decodedBytes = new byte[len];
            int bytesConsumed, bytesWritten;
#pragma warning disable CA1062
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
        RoundtripBase64UTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void RoundtripBase64SSEUtf16()
    {
        RoundtripBase64UTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

   [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void RoundtripBase64ARMUtf16()
    {
        RoundtripBase64UTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void RoundtripBase64UrlUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source).Replace('+', '-').Replace('/', '_');

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
        RoundtripBase64UrlUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void RoundtripBase64UrlSSEUtf16()
    {
        RoundtripBase64UrlUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.SSE.Base64.DecodeFromBase64SSE);
    }

    protected static void BadPaddingBase64UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
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
                        byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
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
                            byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
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
                        byte[] buffer = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(modifiedBase64.ToArray())];
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
        BadPaddingBase64UTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void BadPaddingUTF16Base64SSE()
    {
        BadPaddingBase64UTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void BadPaddingUTF16Base64ARM()
    {
        BadPaddingBase64UTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void DoomedBase64RoundtripUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {
                int bytesConsumed = 0;
                int bytesWritten = 0;
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                char[] base64 = Convert.ToBase64String(source).ToCharArray();

                (char[] base64WithGarbage, int location) = AddGarbage(base64, random);

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64WithGarbage)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.InvalidData, safeResult);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void DoomedBase64RoundtripScalarUTF16()
    {
        DoomedBase64RoundtripUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void DoomedBase64RoundtripSSEUTF16()
    {
        DoomedBase64RoundtripUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void DoomedBase64RoundtripARMUTF16()
    {
        DoomedBase64RoundtripUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected void TruncatedDoomedBase64RoundtripUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 1; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {

                int bytesConsumed = 0;
                int bytesWritten = 0;
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                char[] base64 = Convert.ToBase64String(source).ToCharArray();

                char[] base64Truncated = base64[..^3];  // removing last 3 elements with a view

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Truncated)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64Truncated.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.NeedMoreData, result);
                Assert.Equal((base64.Length - 4) / 4 * 3, bytesWritten);
                Assert.Equal(base64Truncated.Length, bytesConsumed);

                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64Truncated.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.NeedMoreData, safeResult);
                Assert.Equal((base64.Length - 4) / 4 * 3, bytesWritten);
                Assert.Equal(base64Truncated.Length, bytesConsumed);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TruncatedDoomedBase64RoundtripScalarUTF16()
    {
        TruncatedDoomedBase64RoundtripUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void TruncatedDoomedBase64RoundtripSSEUTF16()
    {
        TruncatedDoomedBase64RoundtripUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void TruncatedDoomedBase64RoundtripARMUTF16()
    {
        TruncatedDoomedBase64RoundtripUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void RoundtripBase64WithSpacesUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 0; len < 2048; len++)
        {
            // Initialize source buffer with random bytes
            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            // Encode source to Base64
            string base64String = Convert.ToBase64String(source);
            char[] base64 = base64String.ToCharArray();

            for (int i = 0; i < 5; i++)
            {
                AddSpace(base64.ToList(), random);
            }


            // Prepare buffer for decoded bytes
            byte[] decodedBytes = new byte[len];

            // Call your custom decoding function
            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                base64.AsSpan(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: false);

            // Assert that decoding was successful
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());

            // Safe version not working
            result = Base64WithWhiteSpaceToBinaryFromUTF16(
               base64.AsSpan(), decodedBytes.AsSpan(),
               out bytesConsumed, out bytesWritten, isUrl: false);

            // Assert that decoding was successful
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64WithSpacesScalarUTF16()
    {
        RoundtripBase64WithSpacesUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void RoundtripBase64WithSpacesSSEUTF16()
    {
        RoundtripBase64WithSpacesUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void RoundtripBase64WithSpacesARMUTF16()
    {
        RoundtripBase64WithSpacesUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void AbortedSafeRoundtripBase64UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int offset = 1; offset <= 16; offset += 3)
        {
            for (int len = offset; len < 1024; len++)
            {
                byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Initialize source buffer with random bytes

                string base64String = Convert.ToBase64String(source);

                char[] base64 = base64String.ToCharArray();



                int limitedLength = len - offset; // intentionally too little
                byte[] tooSmallArray = new byte[limitedLength];

                int bytesConsumed = 0;
                int bytesWritten = 0;

                var result = DecodeFromBase64DelegateSafeFromUTF16(
                    base64.AsSpan(), tooSmallArray.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.DestinationTooSmall, result);
                Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());



                // Now let us decode the rest !!!
                ReadOnlySpan<char> base64Remains = base64.AsSpan().Slice(bytesConsumed);

                byte[] decodedRemains = new byte[len - bytesWritten];

                int remainingBytesConsumed = 0;
                int remainingBytesWritten = 0;

                result = DecodeFromBase64DelegateSafeFromUTF16(
                    base64Remains, decodedRemains.AsSpan(),
                    out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

                Assert.Equal(OperationStatus.Done, result);
                Assert.Equal(len, bytesWritten + remainingBytesWritten);
                Assert.Equal(source.Skip(bytesWritten).ToArray(), decodedRemains.ToArray());
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void AbortedSafeRoundtripBase64ScalarUTF16()
    {
        AbortedSafeRoundtripBase64UTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void AbortedSafeRoundtripBase64SSEUTF16()
    {
        AbortedSafeRoundtripBase64UTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "arm64")]
    public void AbortedSafeRoundtripBase64ARMUTF16()
    {
        AbortedSafeRoundtripBase64UTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void AbortedSafeRoundtripBase64WithSpacesUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int offset = 1; offset <= 16; offset += 3)
        {
            for (int len = offset; len < 1024; len++)
            {
                byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Initialize source buffer with random bytes

                string base64String = Convert.ToBase64String(source);

                char[] base64 = base64String.ToCharArray();
                for (int i = 0; i < 5; i++)
                {
                    AddSpace(base64.ToList(), random);
                }

                int limitedLength = len - offset; // intentionally too little
                byte[] tooSmallArray = new byte[limitedLength];

                int bytesConsumed = 0;
                int bytesWritten = 0;

                var result = DecodeFromBase64DelegateSafeFromUTF16(
                    base64.AsSpan(), tooSmallArray.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.DestinationTooSmall, result);
                Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());

                // Now let us decode the rest !!!
                ReadOnlySpan<char> base64Remains = base64.AsSpan().Slice(bytesConsumed);

                byte[] decodedRemains = new byte[len - bytesWritten];

                int remainingBytesConsumed = 0;
                int remainingBytesWritten = 0;

                result = DecodeFromBase64DelegateSafeFromUTF16(
                    base64Remains, decodedRemains.AsSpan(),
                    out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

                Assert.Equal(OperationStatus.Done, result);
                Assert.Equal(len, bytesWritten + remainingBytesWritten);
                Assert.Equal(source.Skip(bytesWritten).ToArray(), decodedRemains.ToArray());
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void AbortedSafeRoundtripBase64WithSpacesScalarUTF16()
    {
        AbortedSafeRoundtripBase64WithSpacesUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void AbortedSafeRoundtripBase64WithSpacesSSEUTF16()
    {
        AbortedSafeRoundtripBase64WithSpacesUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void AbortedSafeRoundtripBase64WithSpacesARMUTF16()
    {
        AbortedSafeRoundtripBase64WithSpacesUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void StreamingBase64RoundtripUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        int len = 2048;
        byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
        random.NextBytes(source); // Initialize source buffer with random bytes

        string base64String = Convert.ToBase64String(source);

        char[] base64 = base64String.ToCharArray();

        for (int window = 16; window <= 2048; window += 7)
        {
            // build a buffer with enough space to receive the decoded base64
            int bytesConsumed = 0;
            int bytesWritten = 0;

            byte[] decodedBytes = new byte[len];
            int outpos = 0;
            for (int pos = 0; pos < base64.Length; pos += window)
            {
                int windowsBytes = Math.Min(window, base64.Length - pos);

#pragma warning disable CA1062
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64.AsSpan().Slice(pos, windowsBytes), decodedBytes.AsSpan().Slice(outpos),
                    out bytesConsumed, out bytesWritten, isUrl: false);

                Assert.True(result != OperationStatus.InvalidData);

                if (windowsBytes + pos == base64.Length)
                {

                    // We must check that the last call to base64_to_binary did not
                    // end with an OperationStatus.NeedMoreData error.
                    Assert.Equal(OperationStatus.Done, result);
                }
                else
                {
                    int tailBytesToReprocess = 0;
                    if (result == OperationStatus.NeedMoreData)
                    {
                        tailBytesToReprocess = 1;
                    }
                    else
                    {
                        tailBytesToReprocess = (bytesWritten % 3) == 0 ? 0 : (bytesWritten % 3) + 1;
                    }
                    pos -= tailBytesToReprocess;
                    bytesWritten -= bytesWritten % 3;
                }
                outpos += bytesWritten;
            }
            Assert.Equal(source, decodedBytes);
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void StreamingBase64RoundtripScalarUTF16()
    {
        StreamingBase64RoundtripUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void StreamingBase64RoundtripSSEUTF16()
    {
        StreamingBase64RoundtripUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void StreamingBase64RoundtripARMUTF16()
    {
        StreamingBase64RoundtripUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected static void ReadmeTestUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        int len = 2048;
        string source = new string('a', len);
        char[] base64 = source.ToCharArray();

        // Calculate the required size for 'decoded' to accommodate Base64 decoding
        byte[] decodedBytes = new byte[(len + 3) / 4 * 3];
        int outpos = 0;
        int window = 512;

        for (int pos = 0; pos < base64.Length; pos += window)
        {
            int bytesConsumed = 0;
            int bytesWritten = 0;

            // how many base64 characters we can process in this iteration
            int windowsBytes = Math.Min(window, base64.Length - pos);
#pragma warning disable CA1062 //validate parameter 'Base64WithWhiteSpaceToBinaryFromUTF16' is non-null before using it.
            var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                base64.AsSpan().Slice(pos, windowsBytes), decodedBytes.AsSpan().Slice(outpos),
                out bytesConsumed, out bytesWritten, isUrl: false);

            Assert.True(result != OperationStatus.InvalidData, $"Invalid base64 character at position {pos + bytesConsumed}");

            // If we arrived at the end of the base64 input, we must check that the
            // number of characters processed is a multiple of 4, or that we have a
            // remainder of 0, 2 or 3.                    
            // Eg we must check that the last call to base64_to_binary did not
            // end with an OperationStatus.NeedMoreData error.

            if (windowsBytes + pos == base64.Length)
            {
                Assert.Equal(OperationStatus.Done, result);
            }
            else
            {
                // If we are not at the end, we may have to reprocess either 1, 2 or 3
                // bytes, and to drop the last 0, 2 or 3 bytes decoded.
                int tailBytesToReprocess = 0;
                if (result == OperationStatus.NeedMoreData)
                {
                    tailBytesToReprocess = 1;
                }
                else
                {
                    tailBytesToReprocess = (bytesWritten % 3) == 0 ? 0 : (bytesWritten % 3) + 1;
                }
                pos -= tailBytesToReprocess;
                bytesWritten -= bytesWritten % 3;
                outpos += bytesWritten;
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void ReadmeTestScalarUTF16()
    {
        ReadmeTestUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void ReadmeTestSSEUTF16()
    {
        ReadmeTestUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void ReadmeTestARMUTF16()
    {
        ReadmeTestUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void ReadmeTestSafeUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        int len = 72;
        string source = new string('a', len);
        char[] base64 = source.ToCharArray();

        byte[] decodedBytesTooSmall = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64) / 2]; // Intentionally too small

        int bytesConsumed = 0;
        int bytesWritten = 0;

        var result = DecodeFromBase64DelegateSafeFromUTF16(
            base64.AsSpan(), decodedBytesTooSmall.AsSpan(),
            out bytesConsumed, out bytesWritten, isUrl: false);
        Assert.Equal(OperationStatus.DestinationTooSmall, result);

        // We decoded 'limited_length' bytes to back.
        // Now let us decode the rest !!!        
        byte[] decodedRemains = new byte[len - bytesWritten];
        ReadOnlySpan<char> base64Remains = base64.AsSpan().Slice(bytesConsumed);

        int remainingBytesConsumed = 0;
        int remainingBytesWritten = 0;

        result = DecodeFromBase64DelegateSafeFromUTF16(
            base64Remains, decodedRemains.AsSpan(),
            out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

        Assert.Equal(OperationStatus.Done, result);
        Assert.Equal(base64.Length, remainingBytesConsumed + bytesConsumed);
        Assert.Equal(SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64), remainingBytesWritten + bytesWritten);
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void ReadmeTestSafeScalarUTF16()
    {
        ReadmeTestSafeUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void ReadmeTestSafeSSEUTF16()
    {
        ReadmeTestSafeUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void ReadmeTestSafeARMUTF16()
    {
        ReadmeTestSafeUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected void DoomedBase64AtPos0(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        if (Base64WithWhiteSpaceToBinaryFromUTF16 == null || DecodeFromBase64DelegateSafeFromUTF16 == null || SimdBase64.Scalar. Base64.MaximalBinaryLengthFromBase64Scalar<char> == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }

        List<int> positions = new List<int>();
        for (int i = 0; i < Tables.ToBase64Value.Length; i++)
        {
            if (Tables.ToBase64Value[i] == 255)
            {
                positions.Add(i);
            }
        }
        for (int len = 57; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int i = 0; i < positions.Count; i++)
            {
                int bytesConsumed = 0;
                int bytesWritten = 0;
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                char[] base64 = Convert.ToBase64String(source).ToCharArray();



                (char[] base64WithGarbage, int location) = AddGarbage(base64, random, 0);

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.InvalidData, result);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.InvalidData, safeResult);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void DoomedBase64AtPos0ScalarUTF16()
    {
        DoomedBase64AtPos0(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void DoomedBase64AtPos0SSEUTF16()
    {
        DoomedBase64AtPos0(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void DoomedBase64AtPos0ARMUTF16()
    {
        DoomedBase64AtPos0(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void EnronFilesTestUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        string[] fileNames = Directory.GetFiles("../../../../benchmark/data/email");
        string[] FileContent = new string[fileNames.Length];

        for (int i = 0; i < fileNames.Length; i++)
        {
            FileContent[i] = File.ReadAllText(fileNames[i]);
        }

        foreach (string s in FileContent)
        {
            char[] base64 = s.ToCharArray();

            Span<byte> output = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];
            int bytesConsumed = 0;
            int bytesWritten = 0;

            var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64.AsSpan(), output, out bytesConsumed, out bytesWritten, false);

            int bytesConsumedScalar = 0;
            int bytesWrittenScalar = 0;

            var resultScalar = DecodeFromBase64DelegateSafeFromUTF16(base64.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

            Assert.True(result == resultScalar);
            Assert.True(result == OperationStatus.Done);
            Assert.True(bytesConsumed== bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
            Assert.True(bytesWritten== bytesWrittenScalar);
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void EnronFilesTestScalarUTF16()
    {
        EnronFilesTestUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void EnronFilesTestSSEUTF16()
    {
        EnronFilesTestUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void EnronFilesTestARMUTF16()
    {
        EnronFilesTestUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void SwedenZoneBaseFileTestUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        string FilePath = "../../../../benchmark/data/dns/swedenzonebase.txt";
        // Read the contents of the file
        string fileContent = File.ReadAllText(FilePath);

        // Convert file content to byte array (assuming it's base64 encoded)
        char[] base64Bytes = fileContent.ToCharArray();

        Span<byte> output = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64Bytes)];


        // Decode the base64 content
        int bytesConsumed, bytesWritten;
        var result = Base64WithWhiteSpaceToBinaryFromUTF16(base64Bytes, output, out bytesConsumed, out bytesWritten, false);

        // Assert that the decoding was successful

        int bytesConsumedScalar = 0;
        int bytesWrittenScalar = 0;

        var resultScalar = DecodeFromBase64DelegateSafeFromUTF16(base64Bytes.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

        Assert.True( result == resultScalar,"result != resultScalar");
        Assert.True(bytesConsumed== bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
        Assert.True(bytesWritten == bytesWrittenScalar, $"bytesWritten: {bytesWritten},bytesWrittenScalar:{bytesWrittenScalar}");
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void SwedenZoneBaseFileTestScalarUTF16()
    {
        SwedenZoneBaseFileTestUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void SwedenZoneBaseFileTestSSEUTF16()
    {
        SwedenZoneBaseFileTestUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void SwedenZoneBaseFileTestARMUTF16()
    {
        SwedenZoneBaseFileTestUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected void DoomedPartialBufferUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16, DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {
        char[] VectorToBeCompressed = new char[] {
        (char)0x6D,(char) 0x6A,(char) 0x6D,(char) 0x73,(char) 0x41,(char) 0x71,(char) 0x39,(char) 0x75,
        (char)0x76,(char) 0x6C,(char) 0x77,(char) 0x48,(char) 0x20,(char) 0x77,(char) 0x33,(char) 0x53
    };
        
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {
                int bytesConsumed = 0;
                int bytesWritten = 0;

                int bytesConsumedSafe = 0;
                int bytesWrittenSafe = 0;

#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                char[] base64 = Convert.ToBase64String(source).ToCharArray();


                (char[] base64WithGarbage, int location) = AddGarbage(base64, random);

                // Insert 1 to 5 copies of the vector right before the garbage
                int numberOfCopies = random.Next(1, 6); // Randomly choose 1 to 5 copies
                List<char> base64WithGarbageAndTrigger = new List<char>(base64WithGarbage);
                int insertPosition = location; // Insert right before the garbage

                for (int i = 0; i < numberOfCopies; i++)
                {
                    base64WithGarbageAndTrigger.InsertRange(insertPosition, VectorToBeCompressed);
                    insertPosition += VectorToBeCompressed.Length;
                }

                // Update the location to reflect the new position of the garbage byte
                location += insertPosition;

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64WithGarbageAndTrigger.ToArray())];

                // Attempt to decode base64 back to binary and assert that it fails
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(insertPosition, bytesConsumed);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
                    out bytesConsumedSafe, out bytesWrittenSafe, isUrl: false);

                Assert.True(result == safeResult);
                Assert.True(bytesConsumedSafe == bytesConsumed, $"bytesConsumedSafe :{bytesConsumedSafe} != bytesConsumed {bytesConsumed}");
                Assert.True(bytesWrittenSafe == bytesWritten,$"bytesWrittenSafe :{bytesWrittenSafe} != bytesWritten {bytesWritten}");

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void DoomedPartialBufferScalarUTF16()
    {
        DoomedPartialBufferUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    [Trait("Category", "sse")]
    public void DoomedPartialBufferSSEUTF16()
    {
        DoomedPartialBufferUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    [Trait("Category", "arm64")]
    public void DoomedPartialBufferARMUTF16()
    {
        DoomedPartialBufferUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM, SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    protected static void Issue511UTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinary)
    {
        ArgumentNullException.ThrowIfNull(Base64WithWhiteSpaceToBinary);

        char[] base64Bytes = [
            (char)0x7f,
            (char)0x57,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x57,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x20,
            (char)0x5a,
            (char)0x20,
            (char)0x5a,
            (char)0x5a,
            (char)0x5a];
        ReadOnlySpan<char> base64Span = new ReadOnlySpan<char>(base64Bytes);
        int bytesConsumed;
        int bytesWritten;
        byte[] buffer = new byte[48];
        var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
        Assert.Equal(OperationStatus.InvalidData, result);

    }

    [Fact]
    [Trait("Category", "scalar")]
    public void Issue511ScalarUTF16()
    {
        Issue511UTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar);
    }


    [Trait("Category", "sse")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    public void Issue511SSEUTF16()
    {
        Issue511UTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE);
    }
    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void Issue511ARMUTF16()
    {
        Issue511UTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM);
    }

    protected void TruncatedCharErrorUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16,DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {

        string badNonASCIIString = "♡♡♡♡";

        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {
                int bytesConsumed = 0;
                int bytesWritten = 0;
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                string base64 = Convert.ToBase64String(source);

                int location = random.Next(0, base64.Length + 1)/4;
                char[] base64WithGarbage = base64.Insert(location, badNonASCIIString).ToCharArray();

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64WithGarbage)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.InvalidData, safeResult);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

            }
        }

        
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TruncatedCharErrorScalarUTF16()
    {
        TruncatedCharErrorUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [Trait("Category", "sse")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    public void TruncatedCharErrorUTF16SSE()
    {
        TruncatedCharErrorUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TruncatedCharErrorUTF16ARM()
    {
        TruncatedCharErrorUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    protected void TruncatedCharErrorUrlUTF16(Base64WithWhiteSpaceToBinaryFromUTF16 Base64WithWhiteSpaceToBinaryFromUTF16,DecodeFromBase64DelegateSafeFromUTF16 DecodeFromBase64DelegateSafeFromUTF16)
    {

        string badNonASCIIString = "♡♡♡♡";

        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {
                int bytesConsumed = 0;
                int bytesWritten = 0;
#pragma warning disable CA5394 // Do not use insecure randomness
                random.NextBytes(source); // Generate random bytes for source

                string base64 = Convert.ToBase64String(source).Replace('+', '-').Replace('/', '_');

                int location = random.Next(0, base64.Length + 1)/4;

                char[] base64WithGarbage = base64.Insert(location, badNonASCIIString).ToCharArray();

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64WithGarbage)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinaryFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: true);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafeFromUTF16(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: true);
                Assert.Equal(OperationStatus.InvalidData, safeResult);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

            }
        }

        
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TruncatedCharErrorUrlScalarUTF16()
    {
        TruncatedCharErrorUrlUTF16(SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }


    [Trait("Category", "sse")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Sse)]
    public void TruncatedCharErrorUrlUTF16SSE()
    {
        TruncatedCharErrorUrlUTF16(SimdBase64.SSE.Base64.DecodeFromBase64SSE,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void TruncatedCharErrorUrlUTF16ARM()
    {
        TruncatedCharErrorUrlUTF16(SimdBase64.Arm.Base64.DecodeFromBase64ARM,SimdBase64.Scalar.Base64.SafeBase64ToBinaryWithWhiteSpace);
    }

}








