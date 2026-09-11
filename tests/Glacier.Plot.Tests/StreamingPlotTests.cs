namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Core;
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
}
