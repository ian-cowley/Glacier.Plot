namespace Glacier.Plot.Tests;

using System;
using System.Linq;
using Glacier.Graphics;
using Glacier.Graphics.Raster;
using Glacier.Plot.Core;
using Glacier.Plot.Statistical;
using Xunit;

public class BoxenPlotTests
{
    [Fact]
    public void BoxenPlot_ConstructsAndCalculatesLimits()
    {
        float[] samples = Enumerable.Range(1, 1000).Select(i => (float)i).ToArray();
        var plot = new BoxenPlot(centerX: 5.0f, samples, baseWidth: 0.8f);

        var limits = plot.GetLimits();
        Assert.True(limits.XMin < 5.0f);
        Assert.True(limits.XMax > 5.0f);
        Assert.True(limits.YMin <= 1.0f);
        Assert.True(limits.YMax >= 1000.0f);
    }

    [Fact]
    public void BoxenPlot_EmptySamples_ReturnsDefaultLimitsWithoutThrowing()
    {
        var plot = new BoxenPlot(centerX: 0f, ReadOnlySpan<float>.Empty);
        var limits = plot.GetLimits();
        Assert.Equal(0, limits.XMin);
        Assert.Equal(1, limits.XMax);
    }

    [Fact]
    public void BoxenPlot_RendersOntoCanvas_Succeeds()
    {
        float[] samples = Enumerable.Range(1, 500).Select(i => (float)Math.Sin(i)).ToArray();
        var plot = new BoxenPlot(centerX: 2.0f, samples, baseWidth: 0.5f, maxLevels: 3);

        using var framebuffer = new LinearFramebuffer(400, 300);
        using var canvas = new CpuGraphicsCanvas(framebuffer);
        var dims = new PlotDimensions(400, 300);
        var converter = new CoordinateConverter(dims, new AxisLimits(0, 4, -1.5, 1.5));
        var theme = PlotTheme.Dark;

        plot.Render(canvas, converter, theme);
        // Completed render without throwing exceptions
    }
}
