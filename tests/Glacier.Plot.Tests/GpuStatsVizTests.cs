namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Compute;
using Glacier.Plot.Core;
using Glacier.Plot.Statistical.Compute;
using Glacier.Plot.Statistical.Kernels;
using Xunit;

[Collection("Gpu")]
[Trait("Hardware", "Gpu")]
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
    public void GpuTarget_Amd_ThrowsPlatformNotSupportedException()
    {
        float[] samples = [1f, 2f, 3f, 4f];
        float[] grid = [1f, 2f, 3f];
        float[] out1D = new float[3];
        float[] out2D = new float[9];

        Assert.Throws<PlatformNotSupportedException>(() =>
            GpuStatsAccelerator.EvaluateKde(samples, grid, 0.5f, out1D, GpuTarget.Amd));
        Assert.Throws<PlatformNotSupportedException>(() =>
            GpuStatsAccelerator.EvaluateKde2D(samples, samples, grid, grid, 0.5f, 0.5f, out2D, GpuTarget.Amd));
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

        if (GpuStatsAccelerator.IsNvidiaAvailable)
        {
            CudaPlotContext.ReleaseGpuResources();
            Assert.Equal(0, CudaPlotContext.PooledSlotCount);

            float[] gpuDensity = new float[numGrid];
            KdeKernels.VectorizedKde(samples, grid, bandwidth, gpuDensity, GpuTarget.Nvidia);
            Assert.True(CudaPlotContext.PooledSlotCount >= 1);

            for (int i = 0; i < numGrid; i++)
            {
                Assert.Equal(cpuDensity[i], gpuDensity[i], 1e-3f);
            }
        }
        else
        {
            float[] gpuDensity = new float[numGrid];
            Assert.Throws<PlatformNotSupportedException>(() =>
                KdeKernels.VectorizedKde(samples, grid, bandwidth, gpuDensity, GpuTarget.Nvidia));
        }

        int midIdx = numGrid / 2;
        Assert.True(cpuDensity[midIdx] > cpuDensity[0]);
        Assert.True(cpuDensity[midIdx] > cpuDensity[^1]);
    }

    [Fact]
    public void VectorizedKde2D_CpuAndGpu_MatchExpected()
    {
        int numSamples = 1024;
        int nx = 24;
        int ny = 24;

        float[] xs = new float[numSamples];
        float[] ys = new float[numSamples];
        var rng = new Random(99);
        for (int i = 0; i < numSamples; i++)
        {
            double u1 = Math.Max(rng.NextDouble(), 1e-7);
            double u2 = rng.NextDouble();
            double u3 = Math.Max(rng.NextDouble(), 1e-7);
            double u4 = rng.NextDouble();
            xs[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
            ys[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u3)) * Math.Cos(2.0 * Math.PI * u4));
        }

        float[] xGrid = new float[nx];
        float[] yGrid = new float[ny];
        for (int i = 0; i < nx; i++) xGrid[i] = -3f + i * (6f / (nx - 1));
        for (int i = 0; i < ny; i++) yGrid[i] = -3f + i * (6f / (ny - 1));

        float[] cpuOut = new float[nx * ny];
        float[] autoOut = new float[nx * ny];

        GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, cpuOut, GpuTarget.Cpu);
        GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, autoOut, GpuTarget.Auto);

        for (int i = 0; i < cpuOut.Length; i++)
        {
            Assert.Equal(cpuOut[i], autoOut[i], 1e-3f);
        }

        if (GpuStatsAccelerator.IsNvidiaAvailable)
        {
            CudaPlotContext.ReleaseGpuResources();
            Assert.Equal(0, CudaPlotContext.PooledSlotCount);

            float[] gpuOut = new float[nx * ny];
            GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, gpuOut, GpuTarget.Nvidia);
            Assert.True(CudaPlotContext.PooledSlotCount >= 1);

            for (int i = 0; i < cpuOut.Length; i++)
            {
                Assert.Equal(cpuOut[i], gpuOut[i], 1e-3f);
            }
        }
        else
        {
            float[] gpuOut = new float[nx * ny];
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, gpuOut, GpuTarget.Nvidia));
        }

        int centerIdx = (ny / 2) * nx + (nx / 2);
        Assert.True(cpuOut[centerIdx] > cpuOut[0]);
        Assert.True(cpuOut[centerIdx] > cpuOut[^1]);
    }

    [Fact]
    public void GpuTarget_Nvidia_WhenUnavailable_ThrowsPlatformNotSupportedException()
    {
        float[] samples = [0f, 1f, 2f, 3f];
        float[] grid = [0f, 1.5f, 3f];
        float[] out1D = new float[3];
        float[] out2D = new float[9];

        using (CudaPlotContext.BeginTestNvidiaUnavailableScope())
        {
            Assert.False(GpuStatsAccelerator.IsNvidiaAvailable);
            Assert.False(GpuStatsAccelerator.IsGpuAvailable);

            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuStatsAccelerator.EvaluateKde(samples, grid, 0.5f, out1D, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                KdeKernels.VectorizedKde(samples, grid, 0.5f, out1D, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuStatsAccelerator.EvaluateKde2D(samples, samples, grid, grid, 0.5f, 0.5f, out2D, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                KdeKernels.VectorizedKde2D(samples, samples, grid, grid, 0.5f, 0.5f, out2D, GpuTarget.Nvidia));

            GpuStatsAccelerator.EvaluateKde(samples, grid, 0.5f, out1D, GpuTarget.Auto);
            GpuStatsAccelerator.EvaluateKde2D(samples, samples, grid, grid, 0.5f, 0.5f, out2D, GpuTarget.Auto);
            Assert.True(out1D[1] > 0f);
            Assert.True(out2D[4] > 0f);
        }
    }

    [Fact]
    public void GpuTarget_Nvidia_WhenCudaExecutionFails_ThrowsInvalidOperationException()
    {
        if (!GpuStatsAccelerator.IsNvidiaAvailable)
            return;

        int numSamples = 1024;
        int numGrid = 64;
        int nx = 16;
        int ny = 16;

        float[] xs = new float[numSamples];
        float[] ys = new float[numSamples];
        for (int i = 0; i < numSamples; i++)
        {
            xs[i] = (i % 64) * 0.1f - 3.2f;
            ys[i] = ((i / 64) % 16) * 0.4f - 3.0f;
        }

        float[] grid1D = new float[numGrid];
        for (int i = 0; i < numGrid; i++) grid1D[i] = -3.5f + i * (7.0f / (numGrid - 1));
        float[] xGrid = new float[nx];
        float[] yGrid = new float[ny];
        for (int i = 0; i < nx; i++) xGrid[i] = -3.0f + i * (6.0f / (nx - 1));
        for (int i = 0; i < ny; i++) yGrid[i] = -3.0f + i * (6.0f / (ny - 1));

        float[] cpu1D = new float[numGrid];
        float[] cpu2D = new float[nx * ny];
        GpuStatsAccelerator.EvaluateKde(xs, grid1D, 0.5f, cpu1D, GpuTarget.Cpu);
        GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, cpu2D, GpuTarget.Cpu);

        float[] out1D = new float[numGrid];
        float[] out2D = new float[nx * ny];

        using (CudaPlotContext.BeginTestExecutionFailureScope())
        {
            Assert.Throws<InvalidOperationException>(() =>
                GpuStatsAccelerator.EvaluateKde(xs, grid1D, 0.5f, out1D, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                KdeKernels.VectorizedKde(xs, grid1D, 0.5f, out1D, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, out2D, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                KdeKernels.VectorizedKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, out2D, GpuTarget.Nvidia));

            float[] auto1D = new float[numGrid];
            float[] auto2D = new float[nx * ny];
            GpuStatsAccelerator.EvaluateKde(xs, grid1D, 0.5f, auto1D, GpuTarget.Auto);
            GpuStatsAccelerator.EvaluateKde2D(xs, ys, xGrid, yGrid, 0.5f, 0.5f, auto2D, GpuTarget.Auto);
            Assert.Equal(cpu1D, auto1D);
            Assert.Equal(cpu2D, auto2D);
        }
    }
}
