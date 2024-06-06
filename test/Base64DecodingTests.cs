namespace tests;
using System.Text;
using SimdUnicode;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;


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
    // private static readonly RandomUtf8 generator = new RandomUtf8(1234, 1, 1, 1, 1);
    private static readonly Random rand = new Random();

    // int[] outputLengths = { 128, 192, 256, 320, 384, 448, 512, 576, 640, 704, 768, 832, 896, 960, 1024, 1088, 1152, 1216, 1280, 1344, 1408, 1472, 1536, 1600, 1664, 1728, 1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560, 2624, 2688, 2752, 2816, 2880, 2944, 3008, 3072, 3136, 3200, 3264, 3328, 3392, 3456, 3520, 3584, 3648, 3712, 3776, 3840, 3904, 3968, 4032, 4096, 4160, 4224, 4288, 4352, 4416, 4480, 4544, 4608, 4672, 4736, 4800, 4864, 4928, 4992, 5056, 5120, 5184, 5248, 5312, 5376, 5440, 5504, 5568, 5632, 5696, 5760, 5824, 5888, 5952, 6016, 6080, 6144, 6208, 6272, 6336, 6400, 6464, 6528, 6592, 6656, 6720, 6784, 6848, 6912, 6976, 7040, 7104, 7168, 7232, 7296, 7360, 7424, 7488, 7552, 7616, 7680, 7744, 7808, 7872, 7936, 8000, 8064, 8128, 8192, 8256, 8320, 8384, 8448, 8512, 8576, 8640, 8704, 8768, 8832, 8896, 8960, 9024, 9088, 9152, 9216, 9280, 9344, 9408, 9472, 9536, 9600, 9664, 9728, 9792, 9856, 9920, 9984, 10000 };
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

    public unsafe delegate byte* Base64DecoderDelegate(byte* pInputBuffer, int inputLength, out int utf16CodeUnitCountAdjustment, out int scalarCountAdjustment);


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
                    return false; // If architecture is not covered above, the test is not supported.
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


    
    private void simpleGoodSequences(Base64DecoderDelegate Base64DecoderDelegate)
    {
        string[] goodSequences = {
        "a",
        "\xC3\xB1",
        "\xE2\x82\xA1",
        "\xF0\x90\x8C\xBC",
        "\xC2\x80",
        "\xF0\x90\x80\x80",
        "\xEE\x80\x80",
        "\xEF\xBB\xBF"
    };

        foreach (var seq in goodSequences)
        {
            byte[] input = System.Text.Encoding.UTF8.GetBytes(seq);
            unsafe
            {
                fixed (byte* pInput = input)
                {
                    // Assert.True(ValidateUtf8(input,Base64DecoderDelegate),
                                    // $"Failure in Scalar function: SimdUnicode.UTF8.GetPointerToFirstInvalidByte.Sequence: {seq}");

//                     Assert.True(ValidateCount(input,Base64DecoderDelegate));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "scalar")]
    public void simpleGoodSequencesScalar()
    {
        // simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteScalar);
    }

    [Trait("Category", "avx")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.X64Avx2)]
    public void simpleGoodSequencesAvx2()
    {
        // simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteAvx2);
    }

    [Trait("Category", "arm64")]
    [FactOnSystemRequirementAttribute(TestSystemRequirements.Arm64)]
    public void simpleGoodSequencesArm64()
    {
        // simpleGoodSequences(SimdUnicode.UTF8.GetPointerToFirstInvalidByteArm64);
    }

}


