namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Compute;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using Xunit;

public class GpuPlotTests
{
    [Fact]
    public void GpuAvailability_CanBeQueriedWithoutThrowing()
    {
        bool nvidia = GpuPlotAccelerator.IsNvidiaAvailable;
        bool amd = GpuPlotAccelerator.IsAmdAvailable;
        bool any = GpuPlotAccelerator.IsGpuAvailable;
        Assert.Equal(any, nvidia || amd);
    }

    [Fact]
    public void MinMaxDownsample_CpuAndGpu_MatchExpected()
    {
        int totalPoints = 100_000;
        int targetPixelWidth = 1_000;
        int targetPoints = targetPixelWidth * 2;

        float[] x = new float[totalPoints];
        float[] y = new float[totalPoints];
        for (int i = 0; i < totalPoints; i++)
        {
            x[i] = i * 0.1f;
            y[i] = MathF.Sin(i * 0.05f) * 100.0f + (i % 17) * 2.0f;
        }

        float[] cpuOutX = new float[targetPoints];
        float[] cpuOutY = new float[targetPoints];
        float[] autoOutX = new float[targetPoints];
        float[] autoOutY = new float[targetPoints];

        int cpuPts = GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, cpuOutX, cpuOutY, GpuTarget.Cpu);
        int autoPts = GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, autoOutX, autoOutY, GpuTarget.Auto);

        Assert.Equal(targetPoints, cpuPts);
        Assert.Equal(targetPoints, autoPts);

        for (int i = 0; i < targetPoints; i++)
        {
            Assert.Equal(cpuOutX[i], autoOutX[i], 1e-4f);
            Assert.Equal(cpuOutY[i], autoOutY[i], 1e-4f);
        }
    }

    [Fact]
    public void TransformCoordinates_CpuAndGpu_MatchExpected()
    {
        int n = 100_000;
        float[] xIn = new float[n];
        float[] yIn = new float[n];
        for (int i = 0; i < n; i++)
        {
            xIn[i] = i * 0.01f;
            yIn[i] = MathF.Cos(i * 0.02f) * 50.0f;
        }

        var dims = new PlotDimensions(1920, 1080, 80, 40, 40, 60);
        var limits = new AxisLimits(0, 1000, -60, 60);
        var conv = new CoordinateConverter(dims, limits);

        float[] cpuOutX = new float[n];
        float[] cpuOutY = new float[n];
        float[] autoOutX = new float[n];
        float[] autoOutY = new float[n];

        conv.TransformCoordinates(xIn, yIn, cpuOutX, cpuOutY, GpuTarget.Cpu);
        conv.TransformCoordinates(xIn, yIn, autoOutX, autoOutY, GpuTarget.Auto);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(cpuOutX[i], autoOutX[i], 1e-3f);
            Assert.Equal(cpuOutY[i], autoOutY[i], 1e-3f);
        }
    }

    [Fact]
    public void ComputeBucketAverages_CpuAndGpu_MatchExpected()
    {
        int totalPoints = 100_000;
        int numBuckets = 500;

        float[] xIn = new float[totalPoints];
        float[] yIn = new float[totalPoints];
        for (int i = 0; i < totalPoints; i++)
        {
            xIn[i] = i * 0.5f;
            yIn[i] = (i % 100) * 1.5f;
        }

        float[] cpuAvgX = new float[numBuckets];
        float[] cpuAvgY = new float[numBuckets];
        float[] autoAvgX = new float[numBuckets];
        float[] autoAvgY = new float[numBuckets];

        GpuPlotAccelerator.ComputeBucketAverages(xIn, yIn, numBuckets, cpuAvgX, cpuAvgY, GpuTarget.Cpu);
        GpuPlotAccelerator.ComputeBucketAverages(xIn, yIn, numBuckets, autoAvgX, autoAvgY, GpuTarget.Auto);

        for (int i = 0; i < numBuckets; i++)
        {
            Assert.Equal(cpuAvgX[i], autoAvgX[i], 1e-3f);
            Assert.Equal(cpuAvgY[i], autoAvgY[i], 1e-3f);
        }
    }
}
