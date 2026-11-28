namespace Glacier.Plot.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Glacier.Graphics;
using Glacier.Graphics.Raster;
using Glacier.Plot.Figures;
using Glacier.Plot.Plottables;
using Xunit;

public class StreamingPlotTests
{
    [Fact]
    public void StreamingPlot_WrapsAroundCapacity_Accurately()
    {
        using var streamPlot = new StreamingLinePlot(capacity: 5);

        // Push 10 elements
        for (int i = 0; i < 10; i++)
        {
            streamPlot.Append(i * 10f);
        }

        Assert.Equal(5, streamPlot.Count);
        Assert.Equal(10, streamPlot.TotalPushed);

        var limits = streamPlot.GetLimits();
        // The active elements should be 50, 60, 70, 80, 90
        Assert.Equal(50f, limits.YMin);
        Assert.Equal(90f, limits.YMax);
        Assert.Equal(5, limits.XMin);
        Assert.Equal(9, limits.XMax);
    }

    [Fact]
    public void StreamingPlot_AppendSpan_AndClear_WorkAccurately()
    {
        using var streamPlot = new StreamingLinePlot(capacity: 4);
        streamPlot.Append([1f, 2f, 3f]);
        Assert.Equal(3, streamPlot.Count);
        Assert.Equal(3L, streamPlot.TotalPushed);

        streamPlot.Append([4f, 5f, 6f, 7f, 8f]);
        Assert.Equal(4, streamPlot.Count);
        Assert.Equal(8L, streamPlot.TotalPushed);
        var limits = streamPlot.GetLimits();
        Assert.Equal(5f, limits.YMin);
        Assert.Equal(8f, limits.YMax);

        streamPlot.Clear();
        Assert.Equal(0, streamPlot.Count);
        Assert.Equal(0L, streamPlot.TotalPushed);
    }

    [Fact]
    public void StreamingPlot_RendersIntoFigure_WithoutExceptions()
    {
        using var fig = new Figure { Title = "Live Telemetry" };
        var streamPlot = fig.PlotStream(capacity: 100, label: "Sensor");

        for (int i = 0; i < 150; i++)
        {
            streamPlot.Append((float)Math.Sin(i * 0.1));
        }

        byte[] png = fig.RenderToBytes(400, 300);
        Assert.NotEmpty(png);
    }

    [Fact]
    public async Task StreamingPlot_ConcurrentSpscStress_WithTightAppendAndClear_IsRaceFree()
    {
        using var streamPlot = new StreamingLinePlot(capacity: 256);
        using var fig = new Figure();
        fig.AddPlottable(streamPlot);

        Exception? writerEx = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));

        var writer = Task.Run(() =>
        {
            try
            {
                int i = 0;
                float[] batch = new float[16];
                while (!cts.IsCancellationRequested)
                {
                    streamPlot.Append(MathF.Sin(i * 0.1f));
                    if ((i & 127) == 0)
                    {
                        for (int k = 0; k < batch.Length; k++)
                            batch[k] = MathF.Cos((i + k) * 0.1f);
                        streamPlot.Append(batch);
                    }
                    if ((i & 2047) == 2047)
                    {
                        streamPlot.Clear();
                    }
                    i++;
                }
            }
            catch (Exception ex)
            {
                writerEx = ex;
            }
        });

        while (!cts.IsCancellationRequested)
        {
            var limits = streamPlot.GetLimits();
            Assert.True(limits.IsValid);

            using var fb = new LinearFramebuffer(120, 80);
            using var canvas = new CpuGraphicsCanvas(fb);
            fig.Render(canvas, 120, 80);
        }

        await writer;
        Assert.Null(writerEx);
    }
}
