namespace Glacier.Plot.Benchmarks;

using System;
using BenchmarkDotNet.Attributes;
using Glacier.Plot.Decimation;
using Glacier.Plot.Figures;

[MemoryDiagnoser]
public class DecimationBenchmarks
{
    private float[] _data100k = null!;
    private float[] _data1M = null!;
    private float[] _data10M = null!;

    private float[] _outX = null!;
    private float[] _outY = null!;

    [GlobalSetup]
    public void Setup()
    {
        _data100k = new float[100_000];
        _data1M = new float[1_000_000];
        _data10M = new float[10_000_000];

        for (int i = 0; i < 100_000; i++) _data100k[i] = MathF.Sin(i * 0.01f);
        for (int i = 0; i < 1_000_000; i++) _data1M[i] = MathF.Sin(i * 0.001f);
        for (int i = 0; i < 10_000_000; i++) _data10M[i] = MathF.Sin(i * 0.0001f);

        _outX = new float[1920];
        _outY = new float[1920];
    }

    [Benchmark(Description = "LTTB Decimation 100k pts -> 1920 px")]
    public int Lttb_100k() => LttbKernels.DownsampleUniform(_data100k, 0f, 1f, 1920, _outX, _outY);

    [Benchmark(Description = "LTTB Decimation 1M pts -> 1920 px")]
    public int Lttb_1M() => LttbKernels.DownsampleUniform(_data1M, 0f, 1f, 1920, _outX, _outY);

    [Benchmark(Description = "LTTB Decimation 10M pts -> 1920 px")]
    public int Lttb_10M() => LttbKernels.DownsampleUniform(_data10M, 0f, 1f, 1920, _outX, _outY);

    [Benchmark(Description = "MinMax Decimation 10M pts -> 1920 px")]
    public int MinMax_10M() => MinMaxKernels.DownsampleUniform(_data10M, 0f, 1f, 960, _outX, _outY);
}
