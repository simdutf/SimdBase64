using System;
//using SimdUnicode;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Filters;
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
                Console.WriteLine($"File exists: {fileName}");
                length = new System.IO.FileInfo(fileName).Length;
            }
            else if (Directory.Exists(fileName))
            {
                Console.WriteLine("It's a directory");
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
            public Config()
            {
                AddColumn(new Speed());


                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    Console.WriteLine("ARM64 system detected.");
                    AddFilter(new AnyCategoriesFilter(["arm64", "scalar", "runtime", "gfoidl"]));

                }
                else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    if (Vector512.IsHardwareAccelerated && System.Runtime.Intrinsics.X86.Avx512Vbmi.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX-512 support.");
                        AddFilter(new AnyCategoriesFilter(["avx512", "avx", "sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else if (Avx2.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with AVX2 support.");
                        AddFilter(new AnyCategoriesFilter(["avx", "sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else if (Ssse3.IsSupported)
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) with Sse4.2 support.");
                        AddFilter(new AnyCategoriesFilter(["sse", "scalar", "runtime", "gfoidl"]));
                    }
                    else
                    {
                        Console.WriteLine("X64 system detected (Intel, AMD,...) without relevant SIMD support.");
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
        [Params(//@"data/email/enron1.txt",
                @"data/email/",
                @"data/dns/swedenzonebase.txt")]
        public string? FileName;
        public string[] FileContent;


        public void RunRuntimeDecodingBenchmark(string[] data)
        {
            foreach (string s in FileContent)
            {
                Convert.FromBase64String(s);
            }
        }
        public unsafe void RunGfoidlDecodingBenchmark(string[] data)
        {
            // gfoidl does not appear to always succeed. Note that
            // the decoding was not integrated into the DOTNET runtime.
            foreach (string s in FileContent)
            {
                ReadOnlySpan<char> span = s.ToCharArray();
                int outlen = Base64.Default.GetDecodedLength(span);
                Span<byte> dataout = stackalloc byte[outlen];
                int consumed = 0;
                int written = 0;
                Base64.Default.Decode(span, dataout, out consumed, out written, true);
            }
        }


        [GlobalSetup]
        public void Setup()
        {
            Console.WriteLine($"FileContent : {FileName}");
            if (FileName == "data/dns/swedenzonebase.txt")
            {
                FileContent = File.ReadAllLines(FileName);
            }
            else if (FileName == "data/email/")
            {
                Console.WriteLine($"FileContent : {FileName}");


                string[] fileNames = Directory.GetFiles(FileName);
                FileContent = new string[fileNames.Length];

                for (int i = 0; i < fileNames.Length; i++)
                {
                    Console.WriteLine($"FileContent loading: {fileNames[i]}");

                    FileContent[i] = File.ReadAllText(fileNames[i]);
                }

            }
            else
            {
                FileContent = [];
            }

        }

        [Benchmark]
        [BenchmarkCategory("default", "runtime")]
        public unsafe void DotnetRuntimeBase64RealData()
        {
            RunRuntimeDecodingBenchmark(FileContent);
        }
        [Benchmark]
        [BenchmarkCategory("default", "gfoidl")]
        public unsafe void DotnetGfoildBase64RealData()
        {
            RunGfoidlDecodingBenchmark(FileContent);
        }


    }
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(["--filter", "*"]);
            }
            else
            {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            }
        }
    }

}