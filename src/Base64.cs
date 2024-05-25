using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;

namespace SimdUnicode
{
    public static class Base64
    {
        // public static bool IsAsciiWhiteSpace<T>(T c) where T : IComparable, IComparable<char>
        // {
        //     return c.CompareTo(' ') == 0 || c.CompareTo('\t') == 0 || c.CompareTo('\n') == 0 || c.CompareTo('\r') == 0 || c.CompareTo('\f') == 0;
        // }

        public static bool IsAsciiWhiteSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }

        public enum Base64Options
        {
            Base64Default = 0, // Standard base64 format
            Base64Url = 1      // Base64url format
        }


    }
}
