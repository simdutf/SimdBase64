namespace tests;
using System.Text;
using SimdUnicode;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Buffers;

public class Base64DecodingTests
{
    Random random = new Random(1234567891987);

    private static readonly char[] SpaceCharacters = { ' ', '\t', '\n', '\r' };

    public static void AddSpace(List<byte> list, Random random)
    {
        int index = random.Next(list.Count + 1); // Random index to insert at
        int charIndex = random.Next(SpaceCharacters.Length); // Random space character
        char spaceChar = SpaceCharacters[charIndex];
        byte[] spaceBytes = Encoding.UTF8.GetBytes(new char[] { spaceChar });
        list.Insert(index, spaceBytes[0]);
    }

    private static void PrintListContents(List<byte> list)
{
    // Console.WriteLine("List contents:");
    foreach (var item in list)
    {
        Console.Write(item + " ");
    }
    // Console.WriteLine();
}

    public static (byte[] modifiedArray, int location) AddGarbage(byte[] inputArray, Random gen)
{
    // Convert byte[] to List<byte>
    List<byte> v = new List<byte>(inputArray);

    int len = v.Count;
    int i;

    // Find the position of the first '=' character
    int equalSignIndex = v.FindIndex(c => c == '=');
    if (equalSignIndex != -1)
    {
        len = equalSignIndex; // Adjust the length to before the '='
        // Console.WriteLine("Found equal signs"); //debug
    }

    // Generate a random index to insert at
    i = gen.Next(len + 1);

    // Generate a random garbage character not in the base64 character set
    byte c;
    do
    {
        c = (byte)gen.Next(256); // Generate a random byte value
    } while (c == '=' || SimdUnicode.Base64Tables.tables.ToBase64Value[c] != 255);

    // Console.WriteLine($"Inserting garbage {c} (rendered as {(char)c}) at index {i}");

    // Insert garbage byte into the List<byte>
    v.Insert(i, c);

    // Convert List<byte> back to byte[]
    byte[] modifiedArray = v.ToArray();

    return (modifiedArray, i);
}



    // helper function for debugging: it prints a green byte every 32 bytes and a red byte at a given index 
    static void PrintHexAndBinary(byte[] bytes, int highlightIndex = -1)
    {
        int chunkSize = 16; // 128 bits = 16 bytes

        // Process each chunk for hexadecimal
        Console.Write("Hex: ");
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % chunkSize == 0)
                Console.WriteLine(); // New line after every 16 bytes

            if (i == highlightIndex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{bytes[i]:X2} ");
                Console.ResetColor();
            }
            else if (i % (chunkSize * 2) == 0) // print green every 256 bytes
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{bytes[i]:X2} ");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"{bytes[i]:X2} ");
            }

            if ((i + 1) % chunkSize != 0) Console.Write(" "); // Add space between bytes but not at the end of the line
        }
        Console.WriteLine("\n"); // New line for readability and to separate hex from binary

        // Process each chunk for binary
        Console.Write("Binary: ");
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % chunkSize == 0)
                Console.WriteLine(); // New line after every 16 bytes

            string binaryString = Convert.ToString(bytes[i], 2).PadLeft(8, '0');
            if (i == highlightIndex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{binaryString} ");
                Console.ResetColor();
            }
            else if (i % (chunkSize * 2) == 0) // print green every 256 bytes
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{binaryString} ");
                Console.ResetColor();
            }
            else
            {
                Console.Write($"{binaryString} ");
            }

            if ((i + 1) % chunkSize != 0) Console.Write(" "); // Add space between bytes but not at the end of the line
        }
        Console.WriteLine(); // New line for readability
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

    public delegate OperationStatus DecodeFromBase64Delegate(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);
    public delegate OperationStatus DecodeFromBase64DelegateSafe(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);
    public delegate int MaxBase64ToBinaryLengthDelegate(ReadOnlySpan<byte> input);
    public delegate OperationStatus Base64WithWhiteSpaceToBinary(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);



    public sealed class FactOnSystemRequirementAttribute : FactAttribute
    {
        private TestSystemRequirements RequiredSystems;

        public FactOnSystemRequirementAttribute(TestSystemRequirements requiredSystems)
        {
            RequiredSystems = requiredSystems;

            if (!IsSystemSupported(requiredSystems))
            {
                Skip = "Test is skipped due to not meeting system requirements.";
            }
        }

        private bool IsSystemSupported(TestSystemRequirements requiredSystems)
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


    public sealed class TestIfCondition : FactAttribute
    {
        public TestIfCondition(Func<bool> condition, string skipReason)
        {
            // Only set the Skip property if the condition evaluates to false
            if (!condition.Invoke())
            {
                Skip = skipReason;
            }
        }

        public Func<bool> Condition { get; }
    }

    public void DecodeBase64Cases(DecodeFromBase64Delegate DecodeFromBase64Delegate, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        var cases = new List<byte[]> { new byte[] { 0x53, 0x53 } };
        // Define expected results for each case
        var expectedResults = new List<(OperationStatus, int)> { (OperationStatus.Done, 1) };

        for (int i = 0; i < cases.Count; i++)
        {
            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(cases[i].AsSpan())];
            int bytesConsumed;
            int bytesWritten;

            var result = DecodeFromBase64Delegate(cases[i], buffer, out bytesConsumed, out bytesWritten, true, false);

            Assert.Equal(expectedResults[i].Item1, result);
            Assert.Equal(expectedResults[i].Item2, bytesWritten);
        }
    }



    [Fact]
    [Trait("Category", "scalar")]
    public void DecodeBase64CasesScalar()
    {
        DecodeBase64Cases(Base64.DecodeFromBase64Scalar, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

    public void CompleteDecodeBase64Cases(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("abcd", " Y\fW\tJ\njZ A=\r= "),
    };

        foreach (var (decoded, base64) in cases)
        {
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            // byte[] base64Bytes = Convert.FromBase64String(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true, true);
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
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, true, false);
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


    public void MoreDecodeTests(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
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
            // Console.WriteLine($"----------Starting:{decoded}");
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            // Console.WriteLine($"This is MaxBase64ToBinaryLengthDelegate:{MaxBase64ToBinaryLengthDelegate(base64Span)}");
            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true, false);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);
            // Console.WriteLine($"Buffer contents as string:{Encoding.UTF8.GetString(buffer, 0, bytesWritten)}");
            // PrintHexAndBinary(buffer); //debug
            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }
        // Console.WriteLine("--Safe version--");

        foreach (var (decoded, base64) in cases)
        {
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, true, false);
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

        public void MoreDecodeTestsUrl(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
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
            // Console.WriteLine($"----------Starting:{decoded}");
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            // byte[] base64Bytes = Convert.FromBase64String(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            // Console.WriteLine($"This is MaxBase64ToBinaryLengthDelegate:{MaxBase64ToBinaryLengthDelegate(base64Span)}");
            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true, true);
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(decoded.Length, bytesWritten);
            Assert.Equal(base64.Length, bytesConsumed);
            // Console.WriteLine($"Buffer contents as string:{Encoding.UTF8.GetString(buffer, 0, bytesWritten)}");
            // PrintHexAndBinary(buffer); //debug
            for (int i = 0; i < bytesWritten; i++)
            {
                Assert.Equal(decoded[i], (char)buffer[i]);
            }
        }
        // Console.WriteLine("--Safe version--");

        foreach (var (decoded, base64) in cases)
        {
            byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
            ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
            int bytesConsumed;
            int bytesWritten;

            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)];
            var result = DecodeFromBase64DelegateSafe(base64Span, buffer, out bytesConsumed, out bytesWritten, true, true);
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
    public void MoreDecodeTestsUrlScalar()
    {

        MoreDecodeTestsUrl(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }


    public void RoundtripBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {        
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source);

            byte[] decodedBytes = new byte[len];

            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinary(
                Encoding.UTF8.GetBytes(base64String).AsSpan(), decodedBytes.AsSpan(), 
                out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void RoundtripBase64Scalar()
    {
        RoundtripBase64(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

        public void RoundtripBase64Url(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {        
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
            random.NextBytes(source);

            string base64String = Convert.ToBase64String(source).Replace('+', '-').Replace('/', '_');;

            byte[] decodedBytes = new byte[len];

            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinary(
                Encoding.UTF8.GetBytes(base64String).AsSpan(), decodedBytes.AsSpan(), 
                out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: true);

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

    public void BadPaddingBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        Random random = new Random();
         
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];
            int bytesConsumed; 
            int bytesWritten;

            for (int trial = 0; trial < 10; trial++)
            {
                Console.WriteLine("----------------------------");
                random.NextBytes(source); // Generate random bytes for source

                string base64 = Convert.ToBase64String(source); // Encode source bytes to Base64
                int padding = base64.EndsWith("=") ? 1 : 0;
                padding += base64.EndsWith("==") ? 1 : 0;

                Console.WriteLine($"Padding is {padding}");

                if (padding != 0)
                {
                    try
                    {
                        Console.WriteLine("Adding one padding");
                        // Test adding padding characters should break decoding
                        byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64 + "=");
                        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)]; 
                        for (int i = 0; i < 5; i++) {
                            AddSpace(modifiedBase64.ToList(),random);
                        }

                        var result = Base64WithWhiteSpaceToBinary(
                            modifiedBase64.AsSpan(), buffer.AsSpan(), 
                            out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

                        Assert.Equal(result, OperationStatus.InvalidData);                
                    }
                    catch (FormatException)
                    {
                        if (padding == 2){
                            Console.WriteLine($"Wrong OperationStatus when adding one padding character to TWO padding character");
                        } else if (padding == 1) {
                            Console.WriteLine($"Wrong OperationStatus when adding one padding character to ONE padding character");
                        }
                    }
                
                    if (padding == 2)
                    {
                        try
                        {
                            Console.WriteLine("Removing one padding");
                            // removing one padding characters should break decoding
                            byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64.Substring(0, base64.Length - 1));
                            byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)]; 
                            for (int i = 0; i < 5; i++) {
                                AddSpace(modifiedBase64.ToList(),random);
                            }

                            var result = Base64WithWhiteSpaceToBinary(
                                modifiedBase64.AsSpan(), buffer.AsSpan(), 
                                out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

                            Assert.Equal(result, OperationStatus.InvalidData);                
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine($"Wrong OperationStatus when substracting one padding character");
                        }
                    }
                } else {
                    try
                    {
                        Console.WriteLine("Adding one padding");
                        // Test adding padding characters should break decoding
                        byte[] modifiedBase64 = Encoding.UTF8.GetBytes(base64 + "=");
                        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(modifiedBase64)]; 
                        for (int i = 0; i < 5; i++) {
                            AddSpace(modifiedBase64.ToList(),random);
                        }

                        var result = Base64WithWhiteSpaceToBinary(
                            modifiedBase64.AsSpan(), buffer.AsSpan(), 
                            out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

                        Assert.Equal(result, OperationStatus.InvalidData);
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine($"Wrong OperationStatus when adding one padding character to base64 string with no padding charater");
                        // Expected behavior: Invalid padding characters should throw FormatException
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


    public void DoomedBase64Roundtrip(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        for (int len = 0; len < 2048; len++)
        {
            byte[] source = new byte[len];

            for (int trial = 0; trial < 10; trial++)
            {
                // Console.WriteLine("-------------------------------");
                int bytesConsumed =0;
                int bytesWritten =0;

                random.NextBytes(source); // Generate random bytes for source

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));

                (byte[] base64WithGarbage,int location) = AddGarbage(base64, random);

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinary(
                    base64WithGarbage.AsSpan(), back.AsSpan(), 
                    out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);
                Assert.Equal(result, OperationStatus.InvalidData);
                Assert.Equal(location , bytesConsumed);
                Assert.Equal( location / 4 *3  , bytesWritten);
                
                // Also test safe decoding with a specified back_length
                var safeResult = DecodeFromBase64DelegateSafe(
                    base64WithGarbage.AsSpan(), back.AsSpan(), 
                    out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);
                Assert.Equal(safeResult, OperationStatus.InvalidData);
                Assert.Equal(location,bytesConsumed);
                Assert.Equal( location / 4 *3  , bytesWritten);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void DoomedBase64RoundtripScalar()
    {
        DoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

        public void TruncatedDoomedBase64Roundtrip(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        for (int len = 1; len < 2048; len++)
        {
            byte[] source = new byte[len];
            List<byte> buffer;

            for (int trial = 0; trial < 10; trial++)
            {
                // Console.WriteLine("-------------------------------"); //debug
                int bytesConsumed =0;
                int bytesWritten =0;

                random.NextBytes(source); // Generate random bytes for source

                byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(source));
                
                byte[] base64Truncated = base64[..^3];  // removing last 3 elements with a view

                // Prepare a buffer for decoding the base64 back to binary
                byte[] back = new byte[MaxBase64ToBinaryLengthDelegate(base64Truncated)];

                // Attempt to decode base64 back to binary and assert that it fails with INVALID_BASE64_CHARACTER
                var result = Base64WithWhiteSpaceToBinary(
                    base64Truncated.AsSpan(), back.AsSpan(), 
                    out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);
                Assert.Equal(result, OperationStatus.NeedMoreData);
                Assert.Equal((base64.Length - 4) /4 *3 , bytesWritten);
                Assert.Equal(base64Truncated.Length , bytesConsumed);
                
                var safeResult = DecodeFromBase64DelegateSafe(
                    base64Truncated.AsSpan(), back.AsSpan(), 
                    out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);
                Assert.Equal(safeResult, OperationStatus.NeedMoreData);
                Assert.Equal((base64.Length - 4) /4 *3 , bytesWritten);
                Assert.Equal(base64Truncated.Length , bytesConsumed);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void TruncatedDoomedBase64RoundtripScalar()
    {
        TruncatedDoomedBase64Roundtrip(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }

        public void RoundtripBase64WithSpaces(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {        
        for (int len = 0; len < 2048; len++)
        {
            // Initialize source buffer with random bytes
            byte[] source = new byte[len];
            random.NextBytes(source);

            // Encode source to Base64
            string base64String = Convert.ToBase64String(source);
            byte[] base64 = Encoding.UTF8.GetBytes(base64String);

            for (int i = 0; i < 5; i++) {
                AddSpace(base64.ToList(),random);
            }


            // Prepare buffer for decoded bytes
            // byte[] decodedBytes = new byte[len];
            byte[] decodedBytes = new byte[len];

            // Call your custom decoding function
            int bytesConsumed, bytesWritten;
            var result = Base64WithWhiteSpaceToBinary(
                base64.AsSpan(), decodedBytes.AsSpan(), 
                out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

            // Assert that decoding was successful
            Assert.Equal(OperationStatus.Done, result);
            Assert.Equal(len, bytesWritten);
            Assert.Equal(base64String.Length, bytesConsumed);
            Assert.Equal(source, decodedBytes.AsSpan().ToArray());

            // Safe version not working
             result = Base64WithWhiteSpaceToBinary(
                base64.AsSpan(), decodedBytes.AsSpan(), 
                out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

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

    public void AbortedSafeRoundtripBase64(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary, DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
        for (int offset = 1; offset <= 16; offset+=3) {
            for (int len = offset; len < 1024; len++) {
                Console.WriteLine("-------------------------");
                // Initialize source buffer with random bytes
                byte[] source = new byte[len];
                random.NextBytes(source);

                // Encode source to Base64
                string base64String = Convert.ToBase64String(source);
                byte[] base64 = Encoding.UTF8.GetBytes(base64String);

                int limitedLength = len - offset; // intentionally too little// Create a new array with the limited length
                // int limitedLength = MaxBase64ToBinaryLengthDelegate(base64) - offset; // intentionally too little// Create a new array with the limited length
                byte[] tooSmallArray = new byte[limitedLength];

                Console.WriteLine($"This is limitedLength:{limitedLength}");

                Console.WriteLine($"This is base64.Length:{base64.Length}");
                Console.WriteLine($"This is tooSmallArray.Length:{tooSmallArray.Length}");

                // Call your custom decoding function
                int bytesConsumed, bytesWritten;
                // var result = Base64WithWhiteSpaceToBinary(

                var result = DecodeFromBase64DelegateSafe(
                    base64.AsSpan(), tooSmallArray.AsSpan(), 
                    out bytesConsumed, out bytesWritten, isFinalBlock: false, isUrl: false);
                Assert.Equal(OperationStatus.DestinationTooSmall, result);
                // Assert.Equal(source, tooSmallArray.AsSpan().ToArray());

                // Now let us decode the rest !!!
                // byte[] decodedRemains = new byte[MaxBase64ToBinaryLengthDelegate(base64) - bytesConsumed];

                // result = DecodeFromBase64DelegateSafe(
                //     base64.AsSpan().Slice(bytesConsumed), decodedRemains.AsSpan(), 
                //     out bytesConsumed, out bytesWritten, isFinalBlock: true, isUrl: false);

                // Assert.Equal(OperationStatus.Done, result);
                // Assert.Equal(limitedLength, bytesWritten);
                // Assert.Equal(decodedRemains.Length + limitedLength, bytesConsumed);
                // Assert.Equal(source, decodedRemains.AsSpan().ToArray());

                // ASSERT_EQUAL(r.error, simdutf::error_code::SUCCESS);
                // decodedRemains.resize(decodedRemains.Length);
                // ASSERT_EQUAL(decodedRemains.Length + limitedLength, len);

            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void AbortedSafeRoundtripBase64Scalar()
    {
        AbortedSafeRoundtripBase64(Base64.Base64WithWhiteSpaceToBinaryScalar, Base64.SafeBase64ToBinaryWithWhiteSpace, Base64.MaximalBinaryLengthFromBase64Scalar);
    }
    
}








