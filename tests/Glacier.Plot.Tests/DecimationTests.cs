namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using Xunit;

public class DecimationTests
{
    [Fact]
    public void Lttb_PreservesEndpoints_AndPeakExtrema()
    {
        int n = 1000;
        float[] x = new float[n];
        float[] y = new float[n];

        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = MathF.Sin(i * 0.05f);
        }
        y[500] = 99.0f;

        int target = 50;
        float[] outX = new float[target];
        float[] outY = new float[target];

        int written = LttbKernels.Downsample(x, y, target, outX, outY);

        Assert.Equal(target, written);
        Assert.Equal(x[0], outX[0]);
        Assert.Equal(y[0], outY[0]);
        Assert.Equal(x[n - 1], outX[target - 1]);
        Assert.Equal(y[n - 1], outY[target - 1]);

        bool peakFound = false;
        for (int i = 0; i < target; i++)
        {
            if (Math.Abs(outY[i] - 99.0f) < 0.001f)
            {
                peakFound = true;
                break;
            }
        }
        Assert.True(peakFound, "LTTB must preserve critical signal peak extrema.");
    }

    [Fact]
    public void LttbUniform_ProducesConsistentResults()
    {
        int n = 500;
        float[] y = new float[n];
        for (int i = 0; i < n; i++) y[i] = (float)Math.Cos(i * 0.1);
        y[250] = -50f;

        int target = 40;
        float[] outX = new float[target];
        float[] outY = new float[target];

        int written = LttbKernels.DownsampleUniform(y, 10f, 0.5f, target, outX, outY);

        Assert.Equal(target, written);
        Assert.Equal(10f, outX[0]);
        Assert.Equal(y[0], outY[0]);

        bool troughFound = false;
        for (int i = 0; i < target; i++)
        {
            if (Math.Abs(outY[i] - (-50f)) < 0.001f) troughFound = true;
        }
        Assert.True(troughFound, "LTTB uniform downsampler must preserve sharp troughs.");
    }

    [Fact]
    public void Lttb_BoundaryCases_HandledGracefully()
    {
        float[] x = [1f, 2f, 3f];
        float[] y = [10f, 20f, 30f];
        float[] outX = new float[5];
        float[] outY = new float[5];

        int written = LttbKernels.Downsample(x, y, 5, outX, outY);
        Assert.Equal(3, written);
        Assert.Equal(1f, outX[0]);
        Assert.Equal(3f, outX[2]);

        written = LttbKernels.Downsample(x, y, 1, outX, outY);
        Assert.Equal(1, written);
        Assert.Equal(1f, outX[0]);

        written = LttbKernels.Downsample(x, y, 2, outX, outY);
        Assert.Equal(2, written);
        Assert.Equal(1f, outX[0]);
        Assert.Equal(3f, outX[1]);
    }

    [Fact]
    public void MinMax_DownsamplesCorrectly()
    {
        int n = 200;
        float[] x = new float[n];
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = i % 2 == 0 ? 10f : -10f;
        }

        int targetPixels = 10;
        float[] outX = new float[targetPixels * 2];
        float[] outY = new float[targetPixels * 2];

        int written = MinMaxKernels.Downsample(x, y, targetPixels, outX, outY);
        Assert.True(written > 0 && written <= targetPixels * 2);

        int writtenUniform = MinMaxKernels.DownsampleUniform(y, 0f, 1f, targetPixels, outX, outY);
        Assert.True(writtenUniform > 0 && writtenUniform <= targetPixels * 2);
    }

    [Fact]
    public void AxisLimits_FromData_ComputesCorrectBounds()
    {
        float[] x = [-10f, 0f, 50f];
        float[] y = [100f, -500f, 250f];

        var limits = AxisLimits.FromData(x, y);
        Assert.Equal(-10.0, limits.XMin);
        Assert.Equal(50.0, limits.XMax);
        Assert.Equal(-500.0, limits.YMin);
        Assert.Equal(250.0, limits.YMax);

        var padded = limits.WithPadding(0.1, 0.1);
        Assert.True(padded.XMin < limits.XMin);
        Assert.True(padded.XMax > limits.XMax);
        Assert.True(padded.YMin < limits.YMin);
        Assert.True(padded.YMax > limits.YMax);
    }
}
