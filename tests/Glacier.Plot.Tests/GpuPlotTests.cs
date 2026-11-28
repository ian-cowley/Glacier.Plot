namespace Glacier.Plot.Tests;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Glacier.Plot.Compute;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using Glacier.Plot.Statistical.Compute;
using Xunit;

[CollectionDefinition("Gpu", DisableParallelization = true)]
public sealed class GpuTestCollection
{
}

[Collection("Gpu")]
[Trait("Hardware", "Gpu")]
public class GpuPlotTests
{
    [Fact]
    public void GpuAvailability_CanBeQueriedWithoutThrowing()
    {
        bool nvidia = GpuPlotAccelerator.IsNvidiaAvailable;
        bool amd = GpuPlotAccelerator.IsAmdAvailable;
        bool any = GpuPlotAccelerator.IsGpuAvailable;
        Assert.False(amd);
        Assert.Equal(any, nvidia || amd);
    }

    [Fact]
    public void GpuTarget_Amd_ThrowsPlatformNotSupportedException()
    {
        float[] x = [0f, 1f, 2f, 3f];
        float[] y = [1f, 2f, 3f, 4f];
        float[] ox = new float[4];
        float[] oy = new float[4];
        var conv = new CoordinateConverter(new PlotDimensions(400, 300), new AxisLimits(0, 3, 1, 4));

        Assert.Throws<PlatformNotSupportedException>(() =>
            GpuPlotAccelerator.MinMaxDownsample(x, y, 2, ox, oy, GpuTarget.Amd));
        Assert.Throws<PlatformNotSupportedException>(() =>
            GpuPlotAccelerator.TransformCoordinates(x, y, ox, oy, conv, GpuTarget.Amd));
        Assert.Throws<PlatformNotSupportedException>(() =>
            GpuPlotAccelerator.ComputeBucketAverages(x, y, 2, ox, oy, GpuTarget.Amd));
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

        if (GpuPlotAccelerator.IsNvidiaAvailable)
        {
            CudaPlotContext.ReleaseGpuResources();
            Assert.Equal(0, CudaPlotContext.PooledSlotCount);

            float[] gpuOutX = new float[targetPoints];
            float[] gpuOutY = new float[targetPoints];
            int gpuPts = GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, gpuOutX, gpuOutY, GpuTarget.Nvidia);
            Assert.Equal(targetPoints, gpuPts);
            Assert.True(CudaPlotContext.PooledSlotCount >= 1);

            for (int i = 0; i < targetPoints; i++)
            {
                Assert.Equal(cpuOutX[i], gpuOutX[i], 1e-4f);
                Assert.Equal(cpuOutY[i], gpuOutY[i], 1e-4f);
            }
        }
        else
        {
            float[] gpuOutX = new float[targetPoints];
            float[] gpuOutY = new float[targetPoints];
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, gpuOutX, gpuOutY, GpuTarget.Nvidia));
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

        if (GpuPlotAccelerator.IsNvidiaAvailable)
        {
            CudaPlotContext.ReleaseGpuResources();
            Assert.Equal(0, CudaPlotContext.PooledSlotCount);

            float[] gpuOutX = new float[n];
            float[] gpuOutY = new float[n];
            conv.TransformCoordinates(xIn, yIn, gpuOutX, gpuOutY, GpuTarget.Nvidia);
            Assert.True(CudaPlotContext.PooledSlotCount >= 1);

            for (int i = 0; i < n; i++)
            {
                Assert.Equal(cpuOutX[i], gpuOutX[i], 1e-3f);
                Assert.Equal(cpuOutY[i], gpuOutY[i], 1e-3f);
            }
        }
        else
        {
            float[] gpuOutX = new float[n];
            float[] gpuOutY = new float[n];
            Assert.Throws<PlatformNotSupportedException>(() =>
                conv.TransformCoordinates(xIn, yIn, gpuOutX, gpuOutY, GpuTarget.Nvidia));
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

        if (GpuPlotAccelerator.IsNvidiaAvailable)
        {
            CudaPlotContext.ReleaseGpuResources();
            Assert.Equal(0, CudaPlotContext.PooledSlotCount);

            float[] gpuAvgX = new float[numBuckets];
            float[] gpuAvgY = new float[numBuckets];
            GpuPlotAccelerator.ComputeBucketAverages(xIn, yIn, numBuckets, gpuAvgX, gpuAvgY, GpuTarget.Nvidia);
            Assert.True(CudaPlotContext.PooledSlotCount >= 1);

            for (int i = 0; i < numBuckets; i++)
            {
                Assert.Equal(cpuAvgX[i], gpuAvgX[i], 1e-3f);
                Assert.Equal(cpuAvgY[i], gpuAvgY[i], 1e-3f);
            }
        }
        else
        {
            float[] gpuAvgX = new float[numBuckets];
            float[] gpuAvgY = new float[numBuckets];
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.ComputeBucketAverages(xIn, yIn, numBuckets, gpuAvgX, gpuAvgY, GpuTarget.Nvidia));
        }
    }

    [Fact]
    public void ReleaseGpuResources_AllowsCleanReinitialization()
    {
        bool before = GpuPlotAccelerator.IsNvidiaAvailable;
        GpuPlotAccelerator.ReleaseGpuResources();

        float[] x = new float[1024];
        float[] y = new float[1024];
        for (int i = 0; i < x.Length; i++) { x[i] = i; y[i] = MathF.Sin(i * 0.1f); }
        float[] ox = new float[64];
        float[] oy = new float[64];

        int count = GpuPlotAccelerator.MinMaxDownsample(x, y, 32, ox, oy, GpuTarget.Auto);
        Assert.Equal(64, count);
        Assert.Equal(before, GpuPlotAccelerator.IsNvidiaAvailable);
    }

    [Fact]
    public void GpuTarget_Nvidia_WhenUnavailable_ThrowsPlatformNotSupportedException()
    {
        float[] xSmall = [0f, 1f, 2f, 3f];
        float[] ySmall = [1f, 3f, 2f, 4f];
        float[] oxSmall = new float[8];
        float[] oySmall = new float[8];

        float[] xLarge = new float[64];
        float[] yLarge = new float[64];
        for (int i = 0; i < xLarge.Length; i++)
        {
            xLarge[i] = i;
            yLarge[i] = MathF.Sin(i * 0.1f);
        }
        float[] oxLarge = new float[64];
        float[] oyLarge = new float[64];

        var conv = new CoordinateConverter(new PlotDimensions(400, 300), new AxisLimits(0, 64, -2, 2));

        using (CudaPlotContext.BeginTestNvidiaUnavailableScope())
        {
            Assert.False(GpuPlotAccelerator.IsNvidiaAvailable);
            Assert.False(GpuPlotAccelerator.IsGpuAvailable);

            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.MinMaxDownsample(xSmall, ySmall, 4, oxSmall, oySmall, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.MinMaxDownsample(xLarge, yLarge, 8, oxLarge, oyLarge, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                MinMaxKernels.Downsample(xLarge, yLarge, 8, oxLarge, oyLarge, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.TransformCoordinates(xLarge, yLarge, oxLarge, oyLarge, conv, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                conv.TransformCoordinates(xLarge, yLarge, oxLarge, oyLarge, GpuTarget.Nvidia));
            Assert.Throws<PlatformNotSupportedException>(() =>
                GpuPlotAccelerator.ComputeBucketAverages(xLarge, yLarge, 8, oxLarge, oyLarge, GpuTarget.Nvidia));

            int autoCount = GpuPlotAccelerator.MinMaxDownsample(xLarge, yLarge, 8, oxLarge, oyLarge, GpuTarget.Auto);
            Assert.Equal(16, autoCount);
            conv.TransformCoordinates(xLarge, yLarge, oxLarge, oyLarge, GpuTarget.Auto);
            GpuPlotAccelerator.ComputeBucketAverages(xLarge, yLarge, 8, oxLarge, oyLarge, GpuTarget.Auto);
        }
    }

    [Fact]
    public void GpuTarget_Nvidia_WhenCudaExecutionFails_ThrowsInvalidOperationException()
    {
        if (!GpuPlotAccelerator.IsNvidiaAvailable)
            return;

        int n = 100_000;
        int targetPixelWidth = 500;
        int targetPoints = targetPixelWidth * 2;
        int numBuckets = 250;

        float[] x = new float[n];
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i * 0.1f;
            y[i] = MathF.Sin(i * 0.05f) * 100f;
        }

        var conv = new CoordinateConverter(new PlotDimensions(1920, 1080), new AxisLimits(0, 10000, -100, 100));

        float[] cpuMinMaxX = new float[targetPoints];
        float[] cpuMinMaxY = new float[targetPoints];
        float[] cpuTransX = new float[n];
        float[] cpuTransY = new float[n];
        float[] cpuAvgX = new float[numBuckets];
        float[] cpuAvgY = new float[numBuckets];

        GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, cpuMinMaxX, cpuMinMaxY, GpuTarget.Cpu);
        conv.TransformCoordinates(x, y, cpuTransX, cpuTransY, GpuTarget.Cpu);
        GpuPlotAccelerator.ComputeBucketAverages(x, y, numBuckets, cpuAvgX, cpuAvgY, GpuTarget.Cpu);

        float[] outX = new float[n];
        float[] outY = new float[n];

        using (CudaPlotContext.BeginTestExecutionFailureScope())
        {
            Assert.Throws<InvalidOperationException>(() =>
                GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, outX, outY, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                MinMaxKernels.Downsample(x, y, targetPixelWidth, outX, outY, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                GpuPlotAccelerator.TransformCoordinates(x, y, outX, outY, conv, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                conv.TransformCoordinates(x, y, outX, outY, GpuTarget.Nvidia));
            Assert.Throws<InvalidOperationException>(() =>
                GpuPlotAccelerator.ComputeBucketAverages(x, y, numBuckets, outX, outY, GpuTarget.Nvidia));

            float[] autoMinMaxX = new float[targetPoints];
            float[] autoMinMaxY = new float[targetPoints];
            int autoCount = GpuPlotAccelerator.MinMaxDownsample(x, y, targetPixelWidth, autoMinMaxX, autoMinMaxY, GpuTarget.Auto);
            Assert.Equal(targetPoints, autoCount);
            Assert.Equal(cpuMinMaxX, autoMinMaxX);
            Assert.Equal(cpuMinMaxY, autoMinMaxY);

            float[] autoTransX = new float[n];
            float[] autoTransY = new float[n];
            conv.TransformCoordinates(x, y, autoTransX, autoTransY, GpuTarget.Auto);
            Assert.Equal(cpuTransX, autoTransX);
            Assert.Equal(cpuTransY, autoTransY);

            float[] autoAvgX = new float[numBuckets];
            float[] autoAvgY = new float[numBuckets];
            GpuPlotAccelerator.ComputeBucketAverages(x, y, numBuckets, autoAvgX, autoAvgY, GpuTarget.Auto);
            Assert.Equal(cpuAvgX, autoAvgX);
            Assert.Equal(cpuAvgY, autoAvgY);
        }
    }

    [Fact]
    public void ConcurrentNvidiaExecution_WithReleaseGpuResources_ThrowsNoExceptionsOrCudaWarnings()
    {
        if (!GpuPlotAccelerator.IsNvidiaAvailable)
            return;

        const int totalPoints = 40_000;
        const int targetPixelWidth = 500;
        const int targetPoints = targetPixelWidth * 2;
        const int numSamples = 1_024;
        const int numGrid = 128;
        const float bandwidth = 0.35f;

        float[] x = new float[totalPoints];
        float[] y = new float[totalPoints];
        for (int i = 0; i < totalPoints; i++)
        {
            x[i] = i * 0.1f;
            y[i] = MathF.Sin(i * 0.05f) * 100.0f + (i % 13) * 1.5f;
        }

        float[] samples = new float[numSamples];
        for (int i = 0; i < numSamples; i++)
            samples[i] = -3.0f + (i % 128) * (6.0f / 127.0f);

        float[] grid = new float[numGrid];
        for (int i = 0; i < numGrid; i++)
            grid[i] = -4.0f + i * (8.0f / (numGrid - 1));

        float[] expectedMinMaxX = new float[targetPoints];
        float[] expectedMinMaxY = new float[targetPoints];
        int expectedMinMaxCount = GpuPlotAccelerator.MinMaxDownsample(
            x, y, targetPixelWidth, expectedMinMaxX, expectedMinMaxY, GpuTarget.Cpu);
        Assert.Equal(targetPoints, expectedMinMaxCount);

        float[] expectedKde = new float[numGrid];
        GpuStatsAccelerator.EvaluateKde(samples, grid, bandwidth, expectedKde, GpuTarget.Cpu);

        var exceptions = new ConcurrentQueue<Exception>();
        var cudaWarnings = new ConcurrentQueue<string>();
        using var listener = new CudaWarningCaptureListener(cudaWarnings);
        Trace.Listeners.Add(listener);

        try
        {
            using var startGate = new ManualResetEventSlim(false);
            bool stop = false;
            int workerIterations = 0;
            int releaseCount = 0;

            Thread[] workers = new Thread[3];
            for (int w = 0; w < workers.Length; w++)
            {
                int workerId = w;
                workers[w] = new Thread(() =>
                {
                    float[] outX = new float[targetPoints];
                    float[] outY = new float[targetPoints];
                    float[] outKde = new float[numGrid];
                    startGate.Wait();

                    int iter = workerId;
                    while (!Volatile.Read(ref stop))
                    {
                        try
                        {
                            if ((iter & 1) == 0)
                            {
                                int count = GpuPlotAccelerator.MinMaxDownsample(
                                    x, y, targetPixelWidth, outX, outY, GpuTarget.Nvidia);
                                if (count != expectedMinMaxCount ||
                                    MathF.Abs(outX[0] - expectedMinMaxX[0]) > 1e-3f ||
                                    MathF.Abs(outY[0] - expectedMinMaxY[0]) > 1e-3f ||
                                    MathF.Abs(outY[^1] - expectedMinMaxY[^1]) > 1e-3f)
                                {
                                    throw new InvalidOperationException("Concurrent MinMaxDownsample GPU output mismatch.");
                                }
                            }
                            else
                            {
                                GpuStatsAccelerator.EvaluateKde(samples, grid, bandwidth, outKde, GpuTarget.Nvidia);
                                if (MathF.Abs(outKde[numGrid / 2] - expectedKde[numGrid / 2]) > 1e-3f ||
                                    !float.IsFinite(outKde[0]) ||
                                    !float.IsFinite(outKde[^1]))
                                {
                                    throw new InvalidOperationException("Concurrent EvaluateKde GPU output mismatch.");
                                }
                            }

                            Interlocked.Increment(ref workerIterations);
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                        }

                        iter++;
                    }
                })
                { IsBackground = true };
                workers[w].Start();
            }

            var releaser = new Thread(() =>
            {
                startGate.Wait();
                while (!Volatile.Read(ref stop))
                {
                    try
                    {
                        CudaPlotContext.ReleaseGpuResources();
                        Interlocked.Increment(ref releaseCount);
                    }
                    catch (Exception ex)
                    {
                        exceptions.Enqueue(ex);
                    }

                    Thread.Sleep(25);
                }
            })
            { IsBackground = true };
            releaser.Start();

            startGate.Set();
            var sw = Stopwatch.StartNew();
            while ((sw.ElapsedMilliseconds < 350 ||
                    Volatile.Read(ref releaseCount) < 5 ||
                    Volatile.Read(ref workerIterations) < 6) &&
                   sw.ElapsedMilliseconds < 3_000)
            {
                Thread.Sleep(15);
            }
            Volatile.Write(ref stop, true);

            foreach (var worker in workers)
                worker.Join();
            releaser.Join();

            Assert.Empty(exceptions);
            Assert.Empty(cudaWarnings);
            Assert.True(Volatile.Read(ref releaseCount) >= 5, $"Expected >=5 releases, saw {releaseCount}");
            Assert.True(Volatile.Read(ref workerIterations) >= 6, $"Expected >=6 worker iterations, saw {workerIterations}");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
            CudaPlotContext.ReleaseGpuResources();
        }
    }

    private sealed class CudaWarningCaptureListener : TraceListener
    {
        private readonly ConcurrentQueue<string> _warnings;

        public CudaWarningCaptureListener(ConcurrentQueue<string> warnings) => _warnings = warnings;

        public override bool IsThreadSafe => true;

        public override void Write(string? message) { }

        public override void WriteLine(string? message) { }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType <= TraceEventType.Warning &&
                message != null &&
                message.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
            {
                _warnings.Enqueue(message);
            }
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType <= TraceEventType.Warning && format != null)
            {
                string formatted = args is { Length: > 0 } ? string.Format(format, args) : format;
                if (formatted.Contains("CUDA", StringComparison.OrdinalIgnoreCase))
                    _warnings.Enqueue(formatted);
            }
        }
    }
}
