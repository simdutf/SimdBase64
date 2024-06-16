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



    private const int NumTrials = 100;
    private static readonly Random rand = new Random();

    static int[] outputLengths = { 128, 345, 1000 }; 

    [Flags]
    public enum TestSystemRequirements
    {
        None = 0,
        Arm64 = 1,
        X64Avx512 = 2,
        X64Avx2 = 4,
        X64Sse = 8,
        // Add more as needed
    }

// This seems redundant but I think the tests will be more legible later on. 
public delegate OperationStatus DecodeFromBase64Delegate(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);
public delegate OperationStatus DecodeFromBase64DelegateSafe(ReadOnlySpan<byte> source, Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);
public delegate int MaxBase64ToBinaryLengthDelegate(ReadOnlySpan<byte> input);
public delegate OperationStatus Base64WithWhiteSpaceToBinary(ReadOnlySpan<byte> source,  Span<byte> dest, out int bytesConsumed, out int bytesWritten, bool isFinalBlock, bool isUrl);



    public class FactOnSystemRequirementAttribute : FactAttribute
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


    public class TestIfCondition : FactAttribute
    {
        public TestIfCondition(Func<bool> condition, string skipReason)
        {
            // Only set the Skip property if the condition evaluates to false
            if (!condition.Invoke())
            {
                Skip = skipReason;
            }
        }
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

            var result = DecodeFromBase64Delegate(cases[i],  buffer, out bytesConsumed, out bytesWritten, true, false );

            Assert.Equal(expectedResults[i].Item1, result);
            Assert.Equal(expectedResults[i].Item2, bytesWritten);
        }
    }



    [Fact]
    [Trait("Category", "scalar")]
    public void DecodeBase64CasesScalar()
    {
        DecodeBase64Cases(Base64.DecodeFromBase64Scalar,Base64.MaximalBinaryLengthFromBase64Scalar);
    }






        public void CompleteDecodeBase64Cases(Base64WithWhiteSpaceToBinary Base64WithWhiteSpaceToBinary,DecodeFromBase64DelegateSafe DecodeFromBase64DelegateSafe, MaxBase64ToBinaryLengthDelegate MaxBase64ToBinaryLengthDelegate)
    {
    List<(string decoded, string base64)> cases = new List<(string, string)>
    {
        ("abcd", " Y\fW\tJ\njZ A=\r= ")
    };

    foreach (var (decoded, base64) in cases)
    {
        byte[] base64Bytes = Encoding.UTF8.GetBytes(base64); // Convert base64 string to byte array
        // PrintHexAndBinary(base64Bytes);
        ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes); // Create ReadOnlySpan from the byte array
        int bytesConsumed;
        int bytesWritten;

        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)]; // Pass ReadOnlySpan to method expecting it
        var result = Base64WithWhiteSpaceToBinary(base64Span, buffer, out bytesConsumed, out bytesWritten, true , false);
        Assert.Equal(OperationStatus.Done, result);// This part is buggy
        Assert.Equal(decoded.Length, bytesWritten);
        for (int i = 0; i < bytesWritten; i++)
        {
            Assert.Equal(decoded[i], (char)buffer[i]);
        }
    }

    Console.Write(" -- Safe version: --  ");

    foreach (var (decoded, base64) in cases)
    {
        byte[] base64Bytes = Encoding.UTF8.GetBytes(base64);
        ReadOnlySpan<byte> base64Span = new ReadOnlySpan<byte>(base64Bytes);
        int bytesConsumed;
        int bytesWritten;

        byte[] buffer = new byte[MaxBase64ToBinaryLengthDelegate(base64Span)]; // Pass ReadOnlySpan to method expecting it
        var result = DecodeFromBase64DelegateSafe(base64Span,  buffer ,  out bytesConsumed, out bytesWritten, true , false);
        Assert.Equal(OperationStatus.Done, result);
        Assert.Equal(decoded.Length, bytesWritten);
        
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
            // CompleteDecodeBase64Cases(Base64.DecodeFromBase64Scalar,Base64.SafeDecodeFromBase64Scalar,Base64.MaximalBinaryLengthFromBase64Scalar);
            CompleteDecodeBase64Cases(Base64.Base64WithWhiteSpaceToBinaryScalar,Base64.SafeBase64ToBinaryWithWhiteSpace,Base64.MaximalBinaryLengthFromBase64Scalar);
        }


}


