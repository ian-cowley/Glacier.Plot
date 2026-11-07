namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Statistical.Compute;
using Glacier.Plot.Statistical.Core;
using Glacier.Plot.Statistical.Kernels;
using Xunit;

public class GpuStatsVizTests
{
    [Fact]
    public void GpuAvailability_CanBeQueriedWithoutThrowing()
    {
        bool nvidia = GpuStatsAccelerator.IsNvidiaAvailable;
        bool amd = GpuStatsAccelerator.IsAmdAvailable;
        bool any = GpuStatsAccelerator.IsGpuAvailable;
        Assert.False(amd);
        Assert.Equal(nvidia, any);
    }

    [Fact]
    public void VectorizedKde_CpuAndGpu_MatchExpected()
    {
        int numSamples = 10_000;
        int numGrid = 200;

        float[] samples = new float[numSamples];
        var rng = new Random(42);
        for (int i = 0; i < numSamples; i++)
        {
            // Standard normal via Box-Muller
            float u1 = (float)rng.NextDouble();
            float u2 = (float)rng.NextDouble();
            samples[i] = MathF.Sqrt(-2.0f * MathF.Log(Math.Max(u1, 1e-7f))) * MathF.Cos(2.0f * MathF.PI * u2);
        }

        float[] grid = new float[numGrid];
        float min = -4.0f;
        float max = 4.0f;
        float step = (max - min) / (numGrid - 1);
        for (int i = 0; i < numGrid; i++)
        {
            grid[i] = min + i * step;
        }

        float bandwidth = 0.3f;
        float[] cpuDensity = new float[numGrid];
        float[] autoDensity = new float[numGrid];

        KdeKernels.VectorizedKde(samples, grid, bandwidth, cpuDensity, GpuTarget.Cpu);
        KdeKernels.VectorizedKde(samples, grid, bandwidth, autoDensity, GpuTarget.Auto);

        for (int i = 0; i < numGrid; i++)
        {
            Assert.Equal(cpuDensity[i], autoDensity[i], 1e-3f);
        }

        // Check that Gaussian peak is around grid = 0
        int midIdx = numGrid / 2;
        Assert.True(cpuDensity[midIdx] > cpuDensity[0]);
        Assert.True(cpuDensity[midIdx] > cpuDensity[^1]);
    }
}
