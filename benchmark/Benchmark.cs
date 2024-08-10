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
        public int PriorityInCategory { get; } = 0;
        public bool IsNumeric { get; } = false;
        public UnitType UnitType { get; } = UnitType.Dimensionless;
        public string Legend { get; } = "The speed in gigabytes per second";
    }


    [SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 3)]
    [Config(typeof(Config))]
    public class RealDataBenchmark
    {

        private class Config : ManualConfig
        {
            static bool warned;
            public Config()
            {
                AddColumn(new Speed());


                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    if (!warned)
                    {
                        Console.WriteLine("ARM64 system detected.");
                        warned = true;
                    }
                    AddFilter(new AnyCategoriesFilter(["arm64", "scalar", "runtime", "gfoidl"]));

                }
                else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    if (Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported)
                    {
                        if (!warned)
                        {
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX-512 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["avx512", "avx", "sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else if (Avx2.IsSupported)
                    {
                        if (!warned)
                        {
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX2 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["avx", "sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else if (Ssse3.IsSupported)
                    {
                        if (!warned)
                        {
                            Console.WriteLine("X64 system detected (Intel, AMD,...) with Sse4.2 support.");
                            warned = true;
                        }
                        AddFilter(new AnyCategoriesFilter(["sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else
                    {
                        if (!warned)
                        {
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
              //  @"data/email/",
                @"data/dns/swedenzonebase.txt")]
        public string? FileName;
        public string[] FileContent;
        public int[] DecodedLengths;


        public void RunRuntimeDecodingBenchmark(string[] data, int[] lengths)
        {
            foreach (string s in FileContent)
            {
                Convert.FromBase64String(s);
            }
        }
        public unsafe void RunGfoidlDecodingBenchmark(string[] data, int[] lengths)
        {
            // gfoidl does not appear to always succeed. Note that
            // the decoding was not integrated into the DOTNET runtime.
            for(int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                ReadOnlySpan<char> span = s.ToCharArray();
                int outlen = Base64.Default.GetDecodedLength(span);
                Span<byte> dataout = new byte[outlen];
                int consumed = 0;
                int written = 0;
                Base64.Default.Decode(span, dataout, out consumed, out written, true);
                if(written != lengths[i])
                {
                    Console.WriteLine($"Error: {written} != {lengths[i]}");
                    throw new Exception("Error");
                }
            }
        }

        public unsafe void RunScalarDecodingBenchmark(string[] data, int[] lengths)
        {
            for(int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                byte[] base64 = Encoding.UTF8.GetBytes(s);
                Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.Base64WithWhiteSpaceToBinaryScalar(base64.AsSpan(), output, out bytesConsumed, out bytesWritten, false);
                if(bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
                    throw new Exception("Error");
                }
            }
        }
        
        public unsafe void RunSSEDecodingBenchmark(string[] data, int[] lengths)
        {
            for(int i = 0; i < FileContent.Length; i++)
            {
                string s = FileContent[i];
                byte[] base64 = Encoding.UTF8.GetBytes(s);
                Span<byte> output = new byte[SimdBase64.Base64.MaximalBinaryLengthFromBase64Scalar(base64)];
                int bytesConsumed = 0;
                int bytesWritten = 0;
                SimdBase64.Base64.DecodeFromBase64SSE(base64.AsSpan(), output, out bytesConsumed, out bytesWritten, false);
                if(bytesWritten != lengths[i])
                {
                    Console.WriteLine($"Error: {bytesWritten} != {lengths[i]}");
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
                for (int i = 0; i < FileContent.Length; i++)
                {
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                }
            }
            else if (FileName == "data/email/")
            {
                string[] fileNames = Directory.GetFiles(FileName);
                FileContent = new string[fileNames.Length];
                DecodedLengths = new int[fileNames.Length];

                for (int i = 0; i < fileNames.Length; i++)
                {
                    FileContent[i] = File.ReadAllText(fileNames[i]);
                    DecodedLengths[i] = Convert.FromBase64String(FileContent[i]).Length;
                }

            }
            else
            {
                FileContent = [];
            }

        }

        /*[Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeBase64RealData()
        {
            RunRuntimeDecodingBenchmark(FileContent, DecodedLengths);
        }
        [Benchmark]
        [BenchmarkCategory("default", "gfoidl")]
        public unsafe void DotnetGfoildBase64RealData()
        {
            RunGfoidlDecodingBenchmark(FileContent, DecodedLengths);
        }

        [Benchmark]
        [BenchmarkCategory("default", "scalar")]
        public unsafe void ScalarDecodingRealData()
        {
            RunScalarDecodingBenchmark(FileContent, DecodedLengths);
        }*/


        [Benchmark]
        [BenchmarkCategory("default", "SSE")]
        public unsafe void SSEDecodingRealData()
        {
            RunSSEDecodingBenchmark(FileContent, DecodedLengths);
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