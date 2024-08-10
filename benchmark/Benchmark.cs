using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using System.Text;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Buffers;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Columns;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.CompilerServices;
using gfoidl.Base64;
using System.Buffers.Text;

namespace SimdUnicodeBenchmarks
{


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


    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 3)]
    [Config(typeof(Config))]
    public class RealDataBenchmark
    {
#pragma warning disable CA1812
        private sealed class Config : ManualConfig
        {
            static bool warned;
            public Config()
            {
                AddColumn(new Speed());


                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    if (!warned)
                    {
#pragma warning disable CA1303
                        Console.WriteLine("ARM64 system detected.");
                        warned = true;
                    }
                    AddFilter(new AnyCategoriesFilter(["arm64", "runtime", "gfoidl"]));

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
                        AddFilter(new AnyCategoriesFilter(["avx512", "avx", "sse", "runtime", "gfoidl"]));
                    }
                    else if (Avx2.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX2 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["avx", "sse", "runtime", "gfoidl"]));
                    }
                    else if (Ssse3.IsSupported && Popcnt.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with Ssse3 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["sse", "runtime", "gfoidl"]));
                    }
                    else if (Sse3.IsSupported && Popcnt.IsSupported)
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with Sse3 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["sse", "runtime", "gfoidl"]));
                    }
                    else
                    {
                        if (!warned)
                        {
#pragma warning disable CA1303
                            Console.WriteLine("X64 system detected (Intel, AMD,...) without relevant SIMD support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["scalar", "runtime", "gfoidl"]));
                    }
                }
                else
                {
                    AddFilter(new AnyCategoriesFilter(["scalar", "runtime", "gfoidl"]));
                }

            }
        }
        // Parameters and variables for real data
        [Params(
                @"data/email/",
                @"data/dns/swedenzonebase.txt")]
#pragma warning disable CA1051
        public string? FileName;
#pragma warning disable CS8618
        public string[] FileContent;
        public int[] DecodedLengths;
        public byte[][] output; // precomputed byte outputs (with correct size)
        public byte[][] input; // precomputed byte inputs


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
                SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
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
                SimdBase64.Base64.DecodeFromBase64SSE(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
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
                //string s = FileContent[i];
                byte[] base64 = input[i];
                byte[] dataoutput = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar(base64.AsSpan())];
                //byte[] dataoutput = output[i];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64SSE(base64.AsSpan(), dataoutput, out bytesConsumed, out bytesWritten, false);
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
                for (int i = 0; i < FileContent.Length; i++)
                {
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                    output[i] = new byte[DecodedLengths[i]];
                    input[i] = Encoding.UTF8.GetBytes(FileContent[i]);
                }
            }
            else if (FileName == "data/email/")
            {
                string[] fileNames = Directory.GetFiles(FileName);
                FileContent = new string[fileNames.Length];
                DecodedLengths = new int[fileNames.Length];
                output = new byte[FileContent.Length][];
                input = new byte[FileContent.Length][];

                for (int i = 0; i < fileNames.Length; i++)
                {
                    FileContent[i] = File.ReadAllText(fileNames[i]);
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                    output[i] = new byte[DecodedLengths[i]];
                    input[i] = Encoding.UTF8.GetBytes(FileContent[i]);
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

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeSIMDBase64RealDataWithAllocUTF8()
        {
            RunRuntimeSIMDDecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeBase64RealDataUTF16()
        {
            RunRuntimeDecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }

        // Gfoidl does not work correctly with spaces.
        /*[Benchmark]
        [BenchmarkCategory("default", "gfoidl")]
        public unsafe void DotnetGfoildBase64RealDataUTF16()
        {
            RunGfoidlDecodingBenchmarkUTF16(FileContent, DecodedLengths);
        }*/

        // We almost never want to benchmark scalar decoding.
        /*[Benchmark]
        [BenchmarkCategory("scalar")]
        public unsafe void ScalarDecodingRealDataUTF8()
        {
            RunScalarDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }*/


        [Benchmark]
        [BenchmarkCategory("SSE")]
        public unsafe void SSEDecodingRealDataUTF8()
        {
            RunSSEDecodingBenchmarkUTF8(FileContent, DecodedLengths);
        }

        [Benchmark]
        [BenchmarkCategory("SSE")]
        public unsafe void SSEDecodingRealDataWithAllocUTF8()
        {
            RunSSEDecodingBenchmarkWithAllocUTF8(FileContent, DecodedLengths);
        }

    }
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