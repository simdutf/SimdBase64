using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using System.Text;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Columns;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SimdUnicodeBenchmarks
{

#pragma warning disable CA1515
    public class Speed : IColumn
    {
        static long GetDirectorySize(string folderPath)
        {
            long totalSize = 0;
            DirectoryInfo di = new DirectoryInfo(folderPath);

            foreach (FileInfo fi in di.EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                totalSize += fi.Length;
            }

            return totalSize;
        }
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
#pragma warning disable CA1062
            var ourReport = summary.Reports.First(x => x.BenchmarkCase.Equals(benchmarkCase));
            var fileName = (string)benchmarkCase.Parameters["FileName"];
            long length = 0;
            if (File.Exists(fileName))
            {
                length = new System.IO.FileInfo(fileName).Length;
            }
            else if (Directory.Exists(fileName))
            {
                length = GetDirectorySize(fileName);
            }
            if (ourReport.ResultStatistics is null)
            {
                return "N/A";
            }
            var mean = ourReport.ResultStatistics.Mean;
            return $"{(length / ourReport.ResultStatistics.Mean):#####.00}";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;

        public string Id { get; } = nameof(Speed);
        public string ColumnName { get; } = "Speed (GB/s)";
        public bool AlwaysShow { get; } = true;
        public ColumnCategory Category { get; } = ColumnCategory.Custom;
#pragma warning disable CA1805
        public int PriorityInCategory { get; } = 0;
#pragma warning disable CA1805
        public bool IsNumeric { get; } = false;
        public UnitType UnitType { get; } = UnitType.Dimensionless;
        public string Legend { get; } = "The speed in gigabytes per second";
    }

#pragma warning disable CA1515
    public class DataVolume : IColumn
    {
        static double GetDirectorySize(string folderPath)
        {
            double totalSize = 0;
            DirectoryInfo di = new DirectoryInfo(folderPath);
            long fileCount = di.EnumerateFiles("*.*", SearchOption.AllDirectories).Count();

            foreach (FileInfo fi in di.EnumerateFiles("*.*", SearchOption.AllDirectories))
            {
                totalSize += fi.Length;
            }

            return totalSize / fileCount;
        }
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
#pragma warning disable CA1062
            var ourReport = summary.Reports.First(x => x.BenchmarkCase.Equals(benchmarkCase));
            var fileName = (string)benchmarkCase.Parameters["FileName"];
            double length = 0;
            if (File.Exists(fileName))
            {
                FileInfo fi = new FileInfo(fileName);
                length = fi.Length;
                length /= File.ReadAllLines(fi.FullName).Length;
            }
            else if (Directory.Exists(fileName))
            {
                length = GetDirectorySize(fileName);
            }
            if (ourReport.ResultStatistics is null)
            {
                return "N/A";
            }
            return $"{length / 1000:#####.00}";
        }

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;

        public string Id { get; } = nameof(DataVolume);
        public string ColumnName { get; } = "kiB/input";
        public bool AlwaysShow { get; } = true;
        public ColumnCategory Category { get; } = ColumnCategory.Custom;
#pragma warning disable CA1805
        public int PriorityInCategory { get; } = 0;
#pragma warning disable CA1805
        public bool IsNumeric { get; } = false;
        public UnitType UnitType { get; } = UnitType.Dimensionless;
        public string Legend { get; } = "The average data volume in kilobytes per input";
    }


    [SimpleJob(launchCount: 1, warmupCount: 10, iterationCount: 20)]
    [Config(typeof(Config))]
#pragma warning disable CA1515
    public class RealDataBenchmark
    {
#pragma warning disable CA1812
        private sealed class Config : ManualConfig
        {
            static bool warned;
            public Config()
            {
                AddColumn(new DataVolume());
                AddColumn(new Speed());
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    if (!warned)
                    {
#pragma warning disable CA1303
                        Console.WriteLine("ARM64 system detected.");
                        warned = true;
                    }
                    AddFilter(new AnyCategoriesFilter(["ARM64", "runtime"]));

                }
                else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    if (Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX-512 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["avx512", "AVX", "SSE", "runtime"]));
                    }
                    else if (Avx2.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX2 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["AVX", "SSE", "runtime"]));
                    }
                    else if (Ssse3.IsSupported && Popcnt.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with Ssse3 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["SSE", "runtime"]));
                    }
                    else if (Sse3.IsSupported && Popcnt.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with Sse3 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["SSE", "runtime"]));
                    }
                    else
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) without relevant SIMD support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["scalar", "runtime"]));
                    }
                }
                else
                {
                    AddFilter(new AnyCategoriesFilter(["scalar", "runtime"]));
                }

            }
        }
        // Parameters and variables for real data
        [Params(
                               @"data/email/",
                               @"data/dns/swedenzonebase.txt"
                )]
#pragma warning disable CA1051
        public string? FileName;
#pragma warning disable CS8618
        public string[] FileContent;
        public int[] DecodedLengths;
        public byte[][] output; // precomputed byte outputs (with correct size)
        public byte[][] input; // precomputed byte inputs
        public char[][] input16; // precomputed char inputs


        public void RunRuntimeDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            foreach (string s in FileContent)
            {
                Convert.FromBase64String(s);
            }
        }

        // Note: The runtime decoding uses advanced SIMD instructions, including AVX-512.
        // Thus on systems with > SSSE3 support, it might beat our SSE implementation.
        // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Helper/Base64DecoderHelper.cs
        public void RunRuntimeSIMDDecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                System.Buffers.Text.Base64.DecodeFromUtf8(input[i].AsSpan(), output[i].AsSpan(), out int consumed, out int written);
                if (written != lengths[i])
                {
                    Console.WriteLine($"Error: {written} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        // Note: The runtime decoding uses advanced SIMD instructions, including AVX-512.
        // See https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Buffers/Text/Base64Helper/Base64DecoderHelper.cs
        // Thus on systems with > SSSE3 support, it might beat our SSE implementation.
        public void RunRuntimeSIMDDecodingBenchmarkWithAllocUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                byte[] outputdata = new byte[System.Buffers.Text.Base64.GetMaxDecodedFromUtf8Length(input[i].Length)];
                System.Buffers.Text.Base64.DecodeFromUtf8(input[i].AsSpan(), outputdata.AsSpan(), out int consumed, out int written);
                if (written != lengths[i])
                {
                    Console.WriteLine($"Error: {written} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }
        public unsafe void RunGfoidlDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            // gfoidl does not appear to always succeed. Note that
            // the decoding was not integrated into the DOTNET runtime.
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> span = s.ToCharArray();
                Span<byte> dataout = output[i];
                int consumed = 0;
                int written = 0;
                gfoidl.Base64.Base64.Default.Decode(span, dataout, out consumed, out written, true);
                if (written != lengths[i])
                {
                    Console.WriteLine($"Error: {written} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunScalarDecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                //string s = FileContent[i];
                byte[] base64 = input[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunScalarDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                char[] base64 = input16[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Scalar.Base64.Base64WithWhiteSpaceToBinaryScalar(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunSSEDecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                //string s = FileContent[i];
                byte[] base64 = input[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.SSE.Base64.DecodeFromBase64SSE(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunSSEDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> base64 = s.AsSpan();
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.SSE.Base64.DecodeFromBase64SSE(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunSSEDecodingBenchmarkWithAllocUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                Span<byte> base64 = input[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.SSE.Base64.DecodeFromBase64SSE(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunSSEDecodingBenchmarkWithAllocUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                Span<char> base64 = input16[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.SSE.Base64.DecodeFromBase64SSE(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunAVX2DecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                byte[] base64 = input[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.AVX2.Base64.DecodeFromBase64AVX2(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }


        public unsafe void RunOurDecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                byte[] base64 = input[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunAVX2DecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> base64 = s.AsSpan();
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.AVX2.Base64.DecodeFromBase64AVX2(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }
        public unsafe void RunOurDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> base64 = s.AsSpan();
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunAVX2DecodingBenchmarkWithAllocUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                Span<byte> base64 = input[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.AVX2.Base64.DecodeFromBase64AVX2(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunAVX2DecodingBenchmarkWithAllocUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                Span<char> base64 = input16[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.AVX2.Base64.DecodeFromBase64AVX2(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }


        public unsafe void RunOurDecodingBenchmarkWithAllocUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                Span<byte> base64 = input[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunOurDecodingBenchmarkWithAllocUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                Span<char> base64 = input16[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunARMDecodingBenchmarkUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                //string s = FileContent[i];
                byte[] base64 = input[i];
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Arm.Base64.DecodeFromBase64ARM(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunARMDecodingBenchmarkUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> base64 = s.AsSpan();
                byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Arm.Base64.DecodeFromBase64ARM(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunARMDecodingBenchmarkWithAllocUTF8(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                Span<byte> base64 = input[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<byte>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Arm.Base64.DecodeFromBase64ARM(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunARMDecodingBenchmarkWithAllocUTF16(string[] data, int[] lengths)
        {
            for (int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                Span<char> base64 = input16[i].AsSpan();
                byte[] dataoutput = new byte[SimdBase64.Scalar.Base64.MaximalBinaryLengthFromBase64Scalar<char>(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Arm.Base64.DecodeFromBase64ARM(base64, dataoutput, out bytesConsumed, out bytesWritten, false);
                if (bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
#pragma warning disable CA2201
                    throw new Exception("Error");
                }
            }
        }

        [GlobalSetup]
        public void Setup()
        {
            Console.WriteLine($"FileContent : {FileName}");
            if (FileName == "data/dns/swedenzonebase.txt")
            {
                FileContent = File.ReadAllLines(FileName);
                DecodedLengths = new int[FileContent.Length];
                output = new byte[FileContent.Length][];
                input = new byte[FileContent.Length][];
                input16 = new char[FileContent.Length][];
                for (int i = 0; i < FileContent.Length; i++)
                {
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                    output[i] = new byte[DecodedLengths[i]];
                    input[i] = Encoding.UTF8.GetBytes(FileContent[i]);
                    input16[i] = FileContent[i].ToCharArray();
                }
            }
            else if (FileName == "data/email/")
            {
                string[] fileNames = Directory.GetFiles(FileName);
                FileContent = new string[fileNames.Length];
                DecodedLengths = new int[fileNames.Length];
                output = new byte[FileContent.Length][];
                input = new byte[FileContent.Length][];
                input16 = new char[FileContent.Length][];

                for (int i = 0; i < fileNames.Length; i++)
                {
                    FileContent[i] = File.ReadAllText(fileNames[i]);
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                    output[i] = new byte[DecodedLengths[i]];
                    input[i] = Encoding.UTF8.GetBytes(FileContent[i]);
                    input16[i] = FileContent[i].ToCharArray();
                }

            }
            else
            {
                FileContent = [];
            }

        }

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeSIMDBase64RealDataUTF8()
        {
            RunRuntimeSIMDDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        public unsafe void DotnetRuntimeSIMDBase64RealDataWithAllocUTF8()
        {
            RunRuntimeSIMDDecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }

        public unsafe void DotnetRuntimeBase64RealDataUTF16()
        {
            RunRuntimeDecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }

        public unsafe void SSEDecodingRealDataUTF8()
        {
            RunSSEDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        public unsafe void SSEDecodingRealDataWithAllocUTF8()
        {
            RunSSEDecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }

        public unsafe void AVX2DecodingRealDataUTF8()
        {
            RunAVX2DecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void SimdBase64DecodingRealDataUTF8()
        {
            RunOurDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        public unsafe void AVX2DecodingRealDataWithAllocUTF8()
        {
            RunAVX2DecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }

        public unsafe void ARMDecodingRealDataUTF8()
        {
            RunARMDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        public unsafe void ARMDecodingRealDataWithAllocUTF8()
        {
            RunARMDecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }


        public unsafe void ARMDecodingRealDataUTF16()
        {
            RunARMDecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }


        public unsafe void SSEDecodingRealDataUTF16()
        {
            RunSSEDecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }

        public unsafe void SSEDecodingRealDataWithAllocUTF16()
        {
            RunSSEDecodingBenchmarkWithAllocUTF16(FileContent, DecodedLengths);
        }

        public unsafe void AVX2DecodingRealDataUTF16()
        {
            RunAVX2DecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }

        public unsafe void AVX2DecodingRealDataWithAllocUTF16()
        {
            RunAVX2DecodingBenchmarkWithAllocUTF16(FileContent, DecodedLengths);
        }

    }
#pragma warning disable CA1515
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                args = new string[] { "--filter", "*" };
            }
            var job = Job.Default
                .WithWarmupCount(1)
                .WithMinIterationCount(2)
                .WithMaxIterationCount(10)
                .AsDefault();

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance.AddJob(job).WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(100)));
        }
    }

}