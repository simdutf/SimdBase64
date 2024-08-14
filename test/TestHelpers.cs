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

public partial class Base64DecodingTests
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

    protected static void AddSpace(List<char> list, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);
#pragma warning disable CA5394 // Do not use insecure randomness
        int index = random.Next(list.Count + 1); // Random index to insert at
#pragma warning disable CA5394 // Do not use insecure randomness
        int charIndex = random.Next(SpaceCharacters.Length); // Random space character
        char spaceChar = SpaceCharacters[charIndex];
        list.Insert(index, spaceChar);
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







}








