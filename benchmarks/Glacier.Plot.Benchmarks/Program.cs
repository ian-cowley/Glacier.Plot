namespace Glacier.Plot.Benchmarks;

using System;
using System.Diagnostics;
using BenchmarkDotNet.Running;
using Glacier.Plot.Decimation;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--bdn", StringComparison.OrdinalIgnoreCase))
        {
            BenchmarkRunner.Run<DecimationBenchmarks>();
            return;
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("               GLACIER.PLOT HIGH-SPEED DECIMATION BENCHMARK                    ");
        Console.WriteLine("================================================================================");

        var bench = new DecimationBenchmarks();
        bench.Setup();

        // Warmup
        for (int i = 0; i < 5; i++)
        {
            bench.Lttb_100k();
            bench.Lttb_1M();
        }

        const int iters = 20;

        // 1. 100k points
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++) bench.Lttb_100k();
        sw.Stop();
        double ms100k = sw.Elapsed.TotalMilliseconds / iters;
        Console.WriteLine($"  LTTB Decimation (100,000 pts -> 1,920 px):  {ms100k:F3} ms ({(0.1 / (ms100k / 1000)):F1} M pts/s)");

        // 2. 1M points
        sw.Restart();
        for (int i = 0; i < iters; i++) bench.Lttb_1M();
        sw.Stop();
        double ms1M = sw.Elapsed.TotalMilliseconds / iters;
        Console.WriteLine($"  LTTB Decimation (1,000,000 pts -> 1,920 px): {ms1M:F3} ms ({(1.0 / (ms1M / 1000)):F1} M pts/s)");

        // 3. 10M points
        sw.Restart();
        for (int i = 0; i < 5; i++) bench.Lttb_10M();
        sw.Stop();
        double ms10M = sw.Elapsed.TotalMilliseconds / 5;
        Console.WriteLine($"  LTTB Decimation (10,000,000 pts -> 1,920 px): {ms10M:F3} ms ({(10.0 / (ms10M / 1000)):F1} M pts/s)");

        // 4. MinMax 10M points
        sw.Restart();
        for (int i = 0; i < 10; i++) bench.MinMax_10M();
        sw.Stop();
        double msMinMax = sw.Elapsed.TotalMilliseconds / 10;
        Console.WriteLine($"  MinMax Decimation (10,000,000 pts -> 1,920 px): {msMinMax:F3} ms ({(10.0 / (msMinMax / 1000)):F1} M pts/s)");

        Console.WriteLine("================================================================================");
    }
}
