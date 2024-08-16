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

public class Base64DecodingTests
{
    Random random = new Random(12345680);

    private static readonly char[] SpaceCharacters = { ' ', '\t', '\n', '\r' };
#pragma warning disable CA1002
    protected static void AddSpace(List<byte> list, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);
#pragma warning disable CA5394 // Do not use insecure randomness
        int index = random.Next(list.Count + 1); // Random index to insert at
#pragma warning disable CA5394 // Do not use insecure randomness
        int charIndex = random.Next(SpaceCharacters.Length); // Random space character
        char spaceChar = SpaceCharacters[charIndex];
        byte[] spaceBytes = Encoding.UTF8.GetBytes(new char[] { spaceChar });
        list.Insert(index, spaceBytes[0]);
    }

    public static (byte[] modifiedArray, int location) AddGarbage(
        byte[] inputArray, Random gen, int? specificLocation = null, byte? specificGarbage = null)
    {
        ArgumentNullException.ThrowIfNull(inputArray);
        ArgumentNullException.ThrowIfNull(gen);
        List<byte> v = new List<byte>(inputArray);

        int len = v.Count;
        int i;

        int equalSignIndex = v.FindIndex(c => c == '=');
        if (equalSignIndex != -1)
        {
            len = equalSignIndex; // Adjust the length to before the '='
        }

        if (specificLocation.HasValue && specificLocation.Value < len)
        {
            i = specificLocation.Value;
        }
        else
        {
            i = gen.Next(len + 1);
        }

        byte c;
        if (specificGarbage.HasValue)
        {
            c = specificGarbage.Value;
        }
        else
        {
            do
            {
                c = (byte)gen.Next(256);
            } while (c == '=' || SimdBase64.Tables.ToBase64Value[c] != 255);
        }

        v.Insert(i, c);

        byte[] modifiedArray = v.ToArray();

        return (modifiedArray, i);
    }


    [Flags]
    public enum TestSystemRequirements
    {
        None = 0,
        Arm64 = 1,
        X64Avx512 = 2,
        X64Avx2 = 4,
        X64Sse = 8,
    }

    public delegate OperationStatus DecodeFromBase64DelegateFnc(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
    public delegate OperationStatus DecodeFromBase64DelegateSafe(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);
    public delegate int MaxBase64ToBinaryLengthDelegateFnc(ReadOnlySpan<byte> input);
    public delegate OperationStatus Base64WithWhiteSpaceToBinary(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isUrl);



    protected sealed class FactOnSystemRequirementAttribute : FactAttribute
    {
        private TestSystemRequirements RequiredSystems;
#pragma warning disable CA1019
        public FactOnSystemRequirementAttribute(TestSystemRequirements requiredSystems)
        {
            RequiredSystems = requiredSystems;

            if (!IsSystemSupported(requiredSystems))
            {
                Skip = "Test is skipped due to not meeting system requirements.";
            }
        }

        private static bool IsSystemSupported(TestSystemRequirements requiredSystems)
        {
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.Arm64:
                    return requiredSystems.HasFlag(TestSystemRequirements.Arm64);
                case Architecture.X64:
                    return (requiredSystems.HasFlag(TestSystemRequirements.X64Avx512) && Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512F.IsSupported) ||
                        (requiredSystems.HasFlag(TestSystemRequirements.X64Avx2) && System.Runtime.Intrinsics.X86.Avx2.IsSupported) ||
                        (requiredSystems.HasFlag(TestSystemRequirements.X64Sse) && System.Runtime.Intrinsics.X86.Sse.IsSupported);
                default:
                    return false;
            }
        }
    }


    protected sealed class TestIfCondition : FactAttribute
    {
#pragma warning disable CA1019
        public TestIfCondition(Func<bool> condition, string skipReason)
        {
            ArgumentNullException.ThrowIfNull(condition);
            // Only set the Skip property if the condition evaluates to false
            if (!condition.Invoke())
            {
                Skip = skipReason;
            }
        }

    }


    protected static void DecodeBase64Cases(DecodeFromBase64DelegateFnc DecodeFromBase64Delegate, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (DecodeFromBase64Delegate == null || MaxBase64ToBinaryLengthDelegate == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        var cases = new List<byte[]> { new byte[] { 0x53, 0x53 } };
        // Define expected results for each case
        var expectedResults = new List<(OperationStatus, int)> { (OperationStatus.Done, 1) };

        for (int i = 0; i < cases.Count; i++)
        {
            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(cases[i].AsSpan())];
            int bytesConsumed;
            int bytesWritten;

            var result = DecodeFromBase64Delegate(cases[i], buffer, out bytesConsumed, out bytesWritten, false);

            Assert.Equal(expectedResults[i].Item1, result);
            Assert.Equal(expectedResults[i].Item2, bytesWritten);
        }
    }



    [Fact]
    [Trait("Category", "scalar")]
    public void DecodeBase64CasesScalar()
    {
        DecodeBase64Cases(Base64.DecodeFromBase64SSE, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "SSE")]
    public void DecodeBase64CasesSSE()
    {
        DecodeBase64Cases(Base64.DecodeFromBase64SSE, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void CompleteDecodeBase64Cases(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("abcd", " Y\fW\tJ\njZ A=\r= "),
    };

        foreach (var (decoded, base64) in cases)
        {
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
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
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
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
    public void CompleteDecodeBase64CasesScalar()
    {
        CompleteDecodeBase64Cases(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void CompleteDecodeBase64CasesSSE()
    {
        CompleteDecodeBase64Cases(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    protected static void MoreDecodeTests(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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


            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
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
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, false);
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
    public void MoreDecodeTestsScalar()
    {
        MoreDecodeTests(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "SSE")]
    public void MoreDecodeTestsSSE()
    {
        MoreDecodeTests(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void MoreDecodeTestsUrl(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
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
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, true);
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
    public void MoreDecodeTestsUrlSSE()
    {
        MoreDecodeTestsUrl(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void MoreDecodeTestsUrlScalar()
    {
        MoreDecodeTestsUrl(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void RoundtripBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
        {
#pragma warning disable CA2208
            throw new ArgumentNullException("Unexpected null parameter");
        }
        for (int len = 0; len < 2048; len++)
        {

            byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source);

            byte[] decodedBytes = new byte[len];

            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinary(
                Encoding.UTF8.GetBytes(base64String).AsSpan(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: false);

            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
            Assert.True(len == bytesWritten, $" Expected bytesWritten: {len} , Actual: {bytesWritten}");
            Assert.True(base64String.Length == bytesConsumed, $" Expected bytesConsumed: {base64String.Length} , Actual: {bytesConsumed}");
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64Scalar()
    {
        RoundtripBase64(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void RoundtripBase64SSE()
    {
        RoundtripBase64(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void RoundtripBase64Url(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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
            var result = Base64WithWhiteSpaceToBinary(
                Encoding.UTF8.GetBytes(base64String).AsSpan(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: true);

            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64UrlScalar()
    {
        RoundtripBase64Url(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void RoundtripBase64UrlSSE()
    {
        RoundtripBase64Url(Base64.DecodeFromBase64SSE, Base64.DecodeFromBase64SSE, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void BadPaddingBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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
                        byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64 + "=");
                        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)];
                        for (int i = 0; i < 5; i++)
                        {
                            AddSpace(modifiedBase64.ToList(), random);
                        }

                        var result = Base64WithWhiteSpaceToBinary(
                            modifiedBase64.AsSpan(), buffer.AsSpan(),
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
                            byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64.Substring(0, base64.Length - 1));
                            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)];
                            for (int i = 0; i < 5; i++)
                            {
                                AddSpace(modifiedBase64.ToList(), random);
                            }

                            var result = Base64WithWhiteSpaceToBinary(
                                modifiedBase64.AsSpan(), buffer.AsSpan(),
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
                        byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64 + "=");
                        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)];
                        for (int i = 0; i < 5; i++)
                        {
                            AddSpace(modifiedBase64.ToList(), random);
                        }

                        var result = Base64WithWhiteSpaceToBinary(
                            modifiedBase64.AsSpan(), buffer.AsSpan(),
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
    public void BadPaddingBase64Scalar()
    {
        BadPaddingBase64(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void BadPaddingBase64SSE()
    {
        BadPaddingBase64(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    protected void DoomedBase64Roundtrip(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));

                (byte[] base64WithGarbage, int location) = AddGarbage(base64, random);

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinary(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafe(
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
    public void DoomedBase64RoundtripScalar()
    {
        DoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void DoomedBase64RoundtripSSE()
    {
        DoomedBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }



    protected void TruncatedDoomedBase64Roundtrip(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));

                byte[] base64Truncated = base64[..^3];  // removing last 3 elements with a view

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64Truncated)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinary(
                    base64Truncated.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.NeedMoreData, result);
                Assert.Equal((base64.Length - 4) / 4 * 3, bytesWritten);
                Assert.Equal(base64Truncated.Length, bytesConsumed);

                var safeResult = DecodeFromBase64DelegateSafe(
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
    public void TruncatedDoomedBase64RoundtripScalar()
    {
        TruncatedDoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void TruncatedDoomedBase64RoundtripSSE()
    {
        TruncatedDoomedBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void RoundtripBase64WithSpaces(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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
            byte[] base64 = Encoding.UTF8.GetBytes(base64String);

            for (int i = 0; i < 5; i++)
            {
                AddSpace(base64.ToList(), random);
            }


            // Prepare buffer for decoded bytes
            byte[] decodedBytes = new byte[len];

            // Call your custom decoding function
            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinary(
                base64.AsSpan(), decodedBytes.AsSpan(),
                out bytesConsumed, out bytesWritten, isUrl: false);

            // Assert that decoding was successful
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());

            // Safe version not working
            result = Base64WithWhiteSpaceToBinary(
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
    public void RoundtripBase64WithSpacesScalar()
    {
        RoundtripBase64WithSpaces(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void RoundtripBase64WithSpacesSSE()
    {
        RoundtripBase64WithSpaces(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void AbortedSafeRoundtripBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

                byte[] base64 = Encoding.UTF8.GetBytes(base64String);



                int limitedLength = len - offset; // intentionally too little
                byte[] tooSmallArray = new byte[limitedLength];

                int bytesConsumed = 0;
                int bytesWritten = 0;

                var result = DecodeFromBase64DelegateSafe(
                    base64.AsSpan(), tooSmallArray.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.DestinationTooSmall, result);
                Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());



                // Now let us decode the rest !!!
                ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

                byte[] decodedRemains = new byte[len - bytesWritten];

                int remainingBytesConsumed = 0;
                int remainingBytesWritten = 0;

                result = DecodeFromBase64DelegateSafe(
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
    public void AbortedSafeRoundtripBase64Scalar()
    {
        AbortedSafeRoundtripBase64(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void AbortedSafeRoundtripBase64SSE()
    {
        AbortedSafeRoundtripBase64(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void AbortedSafeRoundtripBase64WithSpaces(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

                byte[] base64 = Encoding.UTF8.GetBytes(base64String);
                for (int i = 0; i < 5; i++)
                {
                    AddSpace(base64.ToList(), random);
                }

                int limitedLength = len - offset; // intentionally too little
                byte[] tooSmallArray = new byte[limitedLength];

                int bytesConsumed = 0;
                int bytesWritten = 0;

                var result = DecodeFromBase64DelegateSafe(
                    base64.AsSpan(), tooSmallArray.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.DestinationTooSmall, result);
                Assert.Equal(source.Take(bytesWritten).ToArray(), tooSmallArray.Take(bytesWritten).ToArray());

                // Now let us decode the rest !!!
                ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

                byte[] decodedRemains = new byte[len - bytesWritten];

                int remainingBytesConsumed = 0;
                int remainingBytesWritten = 0;

                result = DecodeFromBase64DelegateSafe(
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
    public void AbortedSafeRoundtripBase64WithSpacesScalar()
    {
        AbortedSafeRoundtripBase64WithSpaces(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void AbortedSafeRoundtripBase64WithSpacesSSE()
    {
        AbortedSafeRoundtripBase64WithSpaces(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void StreamingBase64Roundtrip(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        int len = 2048;
        byte[] source = new byte[len];
#pragma warning disable CA5394 // Do not use insecure randomness
        random.NextBytes(source); // Initialize source buffer with random bytes

        string base64String = Convert.ToBase64String(source);

        byte[] base64 = Encoding.UTF8.GetBytes(base64String);

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
                var result = Base64WithWhiteSpaceToBinary(
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
    public void StreamingBase64RoundtripScalar()
    {
        StreamingBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void StreamingBase64RoundtripSSE()
    {
        StreamingBase64Roundtrip(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void ReadmeTest(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        int len = 2048;
        string source = new string('a', len);
        byte[] base64 = Encoding.UTF8.GetBytes(source);

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
#pragma warning disable CA1062 //validate parameter 'Base64WithWhiteSpaceToBinary' is non-null before using it.
            var result = Base64WithWhiteSpaceToBinary(
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
    public void ReadmeTestScalar()
    {
        ReadmeTest(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void ReadmeTestSSE()
    {
        ReadmeTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void ReadmeTestSafe(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        int len = 72;
        string source = new string('a', len);
        byte[] base64 = Encoding.UTF8.GetBytes(source);

        byte[] decodedBytesTooSmall = new byte[MaxBase64ToBinaryLengthDelegate(base64) / 2]; // Intentionally too small

        int bytesConsumed = 0;
        int bytesWritten = 0;

        var result = DecodeFromBase64DelegateSafe(
            base64.AsSpan(), decodedBytesTooSmall.AsSpan(),
            out bytesConsumed, out bytesWritten, isUrl: false);
        Assert.Equal(OperationStatus.DestinationTooSmall, result);

        // We decoded 'limited_length' bytes to back.
        // Now let us decode the rest !!!        
        byte[] decodedRemains = new byte[len - bytesWritten];
        ReadOnlySpan<byte> base64Remains = base64.AsSpan().Slice(bytesConsumed);

        int remainingBytesConsumed = 0;
        int remainingBytesWritten = 0;

        result = DecodeFromBase64DelegateSafe(
            base64Remains, decodedRemains.AsSpan(),
            out remainingBytesConsumed, out remainingBytesWritten, isUrl: false);

        Assert.Equal(OperationStatus.Done, result);
        Assert.Equal(base64.Length, remainingBytesConsumed + bytesConsumed);
        Assert.Equal(MaxBase64ToBinaryLengthDelegate(base64), remainingBytesWritten + bytesWritten);
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void ReadmeTestSafeScalar()
    {
        ReadmeTestSafe(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    [Fact]
    [Trait("Category", "sse")]
    public void ReadmeTestSafeSSE()
    {
        ReadmeTestSafe(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected void DoomedBase64AtPos0(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        if (Base64WithWhiteSpaceToBinary == null || DecodeFromBase64DelegateSafe == null || MaxBase64ToBinaryLengthDelegate == null)
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

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));



                (byte[] base64WithGarbage, int location) = AddGarbage(base64, random, 0);

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinary(
                    base64WithGarbage.AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.Equal(OperationStatus.InvalidData, result);
                Assert.Equal(location, bytesConsumed);
                Assert.Equal(location / 4 * 3, bytesWritten);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafe(
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
    public void DoomedBase64AtPos0Scalar()
    {
        DoomedBase64AtPos0(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void DoomedBase64AtPos0SSE()
    {
        DoomedBase64AtPos0(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    protected static void EnronFilesTest(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        string[] fileNames = Directory.GetFiles("../../../../benchmark/data/email");
        string[] FileContent = new string[fileNames.Length];

        for (int i = 0; i < fileNames.Length; i++)
        {
            FileContent[i] = File.ReadAllText(fileNames[i]);
        }

        foreach (string s in FileContent)
        {
            byte[] base64 = Encoding.UTF8.GetBytes(s);

            Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar(base64)];
            int bytesConsumed = 0;
            int bytesWritten = 0;

            var result = Base64WithWhiteSpaceToBinary(base64.AsSpan(), output, out bytesConsumed, out bytesWritten, false);

            int bytesConsumedScalar = 0;
            int bytesWrittenScalar = 0;

            var resultScalar = DecodeFromBase64DelegateSafe(base64.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

            Assert.True(result == resultScalar);
            Assert.True(result == OperationStatus.Done);
            Assert.True(bytesConsumed == bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
            Assert.True(bytesWritten == bytesWrittenScalar);
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void EnronFilesTestScalar()
    {
        EnronFilesTest(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void EnronFilesTestSSE()
    {
        EnronFilesTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    protected static void SwedenZoneBaseFileTest(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        string FilePath = "../../../../benchmark/data/dns/swedenzonebase.txt";
        // Read the contents of the file
        string fileContent = File.ReadAllText(FilePath);

        // Convert file content to byte array (assuming it's base64 encoded)
        byte[] base64Bytes = Encoding.UTF8.GetBytes(fileContent);

        Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar(base64Bytes)];


        // Decode the base64 content
        int bytesConsumed, bytesWritten;
        var result = Base64WithWhiteSpaceToBinary(base64Bytes, output, out bytesConsumed, out bytesWritten, false);

        // Assert that the decoding was successful

        int bytesConsumedScalar = 0;
        int bytesWrittenScalar = 0;

        var resultScalar = DecodeFromBase64DelegateSafe(base64Bytes.AsSpan(), output, out bytesConsumedScalar, out bytesWrittenScalar, false);

        Assert.True(result == resultScalar, "result != resultScalar");
        Assert.True(bytesConsumed == bytesConsumedScalar, $"bytesConsumed: {bytesConsumed},bytesConsumedScalar:{bytesConsumedScalar}");
        Assert.True(bytesWritten == bytesWrittenScalar, $"bytesWritten: {bytesWritten},bytesWrittenScalar:{bytesWrittenScalar}");
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void SwedenZoneBaseFileTestScalar()
    {
        SwedenZoneBaseFileTest(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void SwedenZoneBaseFileTestSSE()
    {
        SwedenZoneBaseFileTest(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }



    protected void DoomedPartialBuffer(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegateFnc MaxBase64ToBinaryLengthDelegate)
    {
        byte[] VectorToBeCompressed = new byte[] {
        0x6D, 0x6A, 0x6D, 0x73, 0x41, 0x71, 0x39, 0x75,
        0x76, 0x6C, 0x77, 0x48, 0x20, 0x77, 0x33, 0x53
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

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));


                (byte[] base64WithGarbage, int location) = AddGarbage(base64, random);

                // Insert 1 to 5 copies of the vector right before the garbage
                int numberOfCopies = random.Next(1, 6); // Randomly choose 1 to 5 copies
                List<byte> base64WithGarbageAndTrigger = new List<byte>(base64WithGarbage);
                int insertPosition = location; // Insert right before the garbage

                for (int i = 0; i < numberOfCopies; i++)
                {
                    base64WithGarbageAndTrigger.InsertRange(insertPosition, VectorToBeCompressed);
                    insertPosition += VectorToBeCompressed.Length;
                }

                // Update the location to reflect the new position of the garbage byte
                location += insertPosition;

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64WithGarbageAndTrigger.ToArray())];

                // Attempt to decode base64 back to binary and assert that it fails
                var result = Base64WithWhiteSpaceToBinary(
                    base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
                    out bytesConsumed, out bytesWritten, isUrl: false);
                Assert.True(OperationStatus.InvalidData == result, $"OperationStatus {result} is not Invalid Data, error at location {location}. ");
                Assert.Equal(insertPosition, bytesConsumed);

                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafe(
                    base64WithGarbageAndTrigger.ToArray().AsSpan(), back.AsSpan(),
                    out bytesConsumedSafe, out bytesWrittenSafe, isUrl: false);

                Assert.True(result == safeResult);
                Assert.True(bytesConsumedSafe == bytesConsumed, $"bytesConsumedSafe :{bytesConsumedSafe} != bytesConsumed {bytesConsumed}");
                Assert.True(bytesWrittenSafe == bytesWritten, $"bytesWrittenSafe :{bytesWrittenSafe} != bytesWritten {bytesWritten}");

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void DoomedPartialBufferScalar()
    {
        DoomedPartialBuffer(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    [Fact]
    [Trait("Category", "sse")]
    public void DoomedPartialBufferSSE()
    {
        DoomedPartialBuffer(Base64.DecodeFromBase64SSE, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }





}








