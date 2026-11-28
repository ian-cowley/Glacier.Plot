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

    [Fact]
    public void MinMax_LargeBucketSimd_MatchesScalarReferenceExactly()
    {
        const int n = 2048;
        const int targetPixels = 16;
        float[] x = new float[n];
        float[] y = new float[n];
        var rng = new Random(1337);
        for (int i = 0; i < n; i++)
        {
            x[i] = i * 0.25f;
            y[i] = (float)(rng.NextDouble() * 200.0 - 100.0);
        }
        // Inject tied extremes inside buckets
        y[45] = -500f;
        y[50] = -500f;
        y[70] = 600f;

        float[] outX = new float[targetPixels * 2];
        float[] outY = new float[targetPixels * 2];
        int actualCount = MinMaxKernels.Downsample(x, y, targetPixels, outX, outY);

        var expX = new float[targetPixels * 2];
        var expY = new float[targetPixels * 2];
        float bucketSize = (float)n / targetPixels;
        int expIdx = 0;
        for (int b = 0; b < targetPixels; b++)
        {
            int start = (int)(b * bucketSize);
            int end = Math.Min((int)((b + 1) * bucketSize), n);
            if (start >= end) continue;
            float minV = y[start], maxV = y[start];
            int minI = start, maxI = start;
            for (int i = start + 1; i < end; i++)
            {
                if (y[i] < minV) { minV = y[i]; minI = i; }
                if (y[i] > maxV) { maxV = y[i]; maxI = i; }
            }
            if (minI <= maxI)
            {
                expX[expIdx] = x[minI]; expY[expIdx++] = minV;
                if (minI != maxI) { expX[expIdx] = x[maxI]; expY[expIdx++] = maxV; }
            }
            else
            {
                expX[expIdx] = x[maxI]; expY[expIdx++] = maxV;
                expX[expIdx] = x[minI]; expY[expIdx++] = minV;
            }
        }

        Assert.Equal(expIdx, actualCount);
        Assert.Equal(expX[..expIdx], outX[..actualCount]);
        Assert.Equal(expY[..expIdx], outY[..actualCount]);
    }

    [Fact]
    public void MinMax_UniformLargeBucketSimd_WithTiesAndNaNs_MatchesScalarReferenceExactly()
    {
        const int n = 2048;
        const int targetPixels = 16;
        const float xStart = 2.0f;
        const float xStep = 0.5f;
        float[] y = new float[n];
        var rng = new Random(2026);
        for (int i = 0; i < n; i++)
        {
            y[i] = (float)(rng.NextDouble() * 100.0 - 50.0);
        }

        // Inject tied extremes + NaN inside buckets
        y[20] = -250f;
        y[35] = -250f;
        y[40] = float.NaN;
        y[90] = 300f;
        y[95] = 300f;

        float[] outX = new float[targetPixels * 2];
        float[] outY = new float[targetPixels * 2];
        int actualCount = MinMaxKernels.DownsampleUniform(y, xStart, xStep, targetPixels, outX, outY);

        var expX = new float[targetPixels * 2];
        var expY = new float[targetPixels * 2];
        float bucketSize = (float)n / targetPixels;
        int expIdx = 0;
        for (int b = 0; b < targetPixels; b++)
        {
            int start = (int)(b * bucketSize);
            int end = Math.Min((int)((b + 1) * bucketSize), n);
            if (start >= end) continue;
            float minV = y[start], maxV = y[start];
            int minI = start, maxI = start;
            for (int i = start + 1; i < end; i++)
            {
                if (y[i] < minV) { minV = y[i]; minI = i; }
                if (y[i] > maxV) { maxV = y[i]; maxI = i; }
            }
            if (minI <= maxI)
            {
                expX[expIdx] = xStart + minI * xStep; expY[expIdx++] = minV;
                if (minI != maxI) { expX[expIdx] = xStart + maxI * xStep; expY[expIdx++] = maxV; }
            }
            else
            {
                expX[expIdx] = xStart + maxI * xStep; expY[expIdx++] = maxV;
                expX[expIdx] = xStart + minI * xStep; expY[expIdx++] = minV;
            }
        }

        Assert.Equal(expIdx, actualCount);
        Assert.Equal(expX[..expIdx], outX[..actualCount]);
        Assert.Equal(expY[..expIdx], outY[..actualCount]);
    }

    [Fact]
    public void MinMax_FirstSimdChunkNaN_MatchesScalarReferenceExactly()
    {
        const int n = 1024;
        const int targetPixels = 8; // bucketSize = 128 (>= 32 -> hits Vector512 / Vector256 / scalar)
        const float xStart = 1.5f;
        const float xStep = 0.25f;

        float[] x = new float[n];
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = xStart + i * xStep;
            y[i] = 10f;
        }

        // Bucket 0 [0, 128): finite y[0], NaNs at start+1, start+7, start+15; ties & extrema in poisoned lanes
        y[0] = 10f;
        y[1] = float.NaN;
        y[7] = float.NaN;
        y[15] = float.NaN;
        y[9] = -500f;   // AVX2 lane 1
        y[17] = -500f;  // AVX-512 lane 1 (tie with index 9 -> must pick index 9)
        y[23] = 750f;   // AVX-512 lane 7 & AVX2 lane 7

        // Bucket 1 [128, 256): finite y[128], NaNs at start+1, start+7, start+15; extrema in lane 15
        y[128] = 10f;
        y[129] = float.NaN;
        y[135] = float.NaN;
        y[143] = float.NaN;
        y[143 + 16] = 850f;  // AVX-512 lane 15 & AVX2 lane 7
        y[143 + 32] = -700f; // AVX-512 lane 15 & AVX2 lane 7

        // Bucket 2 [256, 384): finite y[256], NaN at start+1; unique extrema in AVX-512 lane 1 & AVX2 lane 1
        y[256] = 10f;
        y[257] = float.NaN;
        y[265] = 400f;  // AVX2 lane 1
        y[273] = -400f; // AVX-512 lane 1

        // Bucket 3 [384, 512): starts with float.NaN -> must return (NaN, NaN, 384, 384)
        y[384] = float.NaN;
        y[385] = -1000f;
        y[400] = 1000f;

        // Bucket 4 [512, 640): finite y[512] = 5f, NaNs at start+1, start+7, start+15, all other values equal 5f (tie -> must pick 512)
        for (int i = 512; i < 640; i++) y[i] = 5f;
        y[513] = float.NaN;
        y[519] = float.NaN;
        y[527] = float.NaN;

        float[] outX = new float[targetPixels * 2];
        float[] outY = new float[targetPixels * 2];
        float[] uniformOutX = new float[targetPixels * 2];
        float[] uniformOutY = new float[targetPixels * 2];
        float[] accelOutX = new float[targetPixels * 2];
        float[] accelOutY = new float[targetPixels * 2];

        int count = MinMaxKernels.Downsample(x, y, targetPixels, outX, outY);
        int uniformCount = MinMaxKernels.DownsampleUniform(y, xStart, xStep, targetPixels, uniformOutX, uniformOutY);
        int accelCount = Compute.GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixels, accelOutX, accelOutY, GpuTarget.Cpu);

        var expX = new float[targetPixels * 2];
        var expY = new float[targetPixels * 2];
        var expAccelX = new float[targetPixels * 2];
        var expAccelY = new float[targetPixels * 2];
        float bucketSize = (float)n / targetPixels;
        int expIdx = 0;
        for (int b = 0; b < targetPixels; b++)
        {
            int start = (int)(b * bucketSize);
            int end = Math.Min((int)((b + 1) * bucketSize), n);
            if (start >= end) continue;

            float minV = y[start], maxV = y[start];
            int minI = start, maxI = start;
            for (int i = start + 1; i < end; i++)
            {
                if (y[i] < minV) { minV = y[i]; minI = i; }
                if (y[i] > maxV) { maxV = y[i]; maxI = i; }
            }

            if (minI <= maxI)
            {
                expX[expIdx] = x[minI]; expY[expIdx++] = minV;
                if (minI != maxI) { expX[expIdx] = x[maxI]; expY[expIdx++] = maxV; }
                expAccelX[b * 2] = x[minI]; expAccelY[b * 2] = minV;
                expAccelX[b * 2 + 1] = x[maxI]; expAccelY[b * 2 + 1] = maxV;
            }
            else
            {
                expX[expIdx] = x[maxI]; expY[expIdx++] = maxV;
                expX[expIdx] = x[minI]; expY[expIdx++] = minV;
                expAccelX[b * 2] = x[maxI]; expAccelY[b * 2] = maxV;
                expAccelX[b * 2 + 1] = x[minI]; expAccelY[b * 2 + 1] = minV;
            }
        }

        Assert.Equal(expIdx, count);
        Assert.Equal(expX[..expIdx], outX[..count]);
        Assert.Equal(expY[..expIdx], outY[..count]);

        Assert.Equal(expIdx, uniformCount);
        Assert.Equal(expX[..expIdx], uniformOutX[..uniformCount]);
        Assert.Equal(expY[..expIdx], uniformOutY[..uniformCount]);

        Assert.Equal(targetPixels * 2, accelCount);
        Assert.Equal(expAccelX, accelOutX);
        Assert.Equal(expAccelY, accelOutY);
    }

    [Fact]
    public void GpuPlotAccelerator_CpuSimdBucketAverages_MatchesScalarExact()
    {
        const int n = 4096;
        const int numBuckets = 32;
        float[] x = new float[n];
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i * 0.125f;
            y[i] = MathF.Sin(i * 0.03f) * 20f + 5f;
        }

        float[] avgX = new float[numBuckets];
        float[] avgY = new float[numBuckets];
        Compute.GpuPlotAccelerator.ComputeBucketAverages(x, y, numBuckets, avgX, avgY, GpuTarget.Cpu);

        float bucketSize = (float)n / numBuckets;
        for (int b = 0; b < numBuckets; b++)
        {
            int start = (int)(b * bucketSize);
            int end = Math.Min((int)((b + 1) * bucketSize), n);
            double sx = 0, sy = 0;
            for (int i = start; i < end; i++) { sx += x[i]; sy += y[i]; }
            float ex = (float)(sx / (end - start));
            float ey = (float)(sy / (end - start));
            Assert.Equal(ex, avgX[b], 1e-4f);
            Assert.Equal(ey, avgY[b], 1e-4f);
        }
    }
}
