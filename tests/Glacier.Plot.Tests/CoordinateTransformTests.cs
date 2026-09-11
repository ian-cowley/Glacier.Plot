namespace Glacier.Plot.Tests;

using Glacier.Plot.Core;
using Xunit;

public class CoordinateTransformTests
{
    [Fact]
    public void CoordinateConverter_MapsExtremesAccurately()
    {
        var dims = new PlotDimensions(1000, 500, 100, 50, 40, 60);
        var limits = new AxisLimits(0, 100, -50, 50);
        var conv = new CoordinateConverter(dims, limits);

        // DataLeft = 100, DataRight = 950
        // DataTop = 40, DataBottom = 440

        Assert.Equal(100f, conv.GetPixelX(0));
        Assert.Equal(950f, conv.GetPixelX(100));

        Assert.Equal(440f, conv.GetPixelY(-50));
        Assert.Equal(40f, conv.GetPixelY(50));

        // Midpoint
        Assert.Equal((100f + 950f) * 0.5f, conv.GetPixelX(50), 3);
        Assert.Equal((440f + 40f) * 0.5f, conv.GetPixelY(0), 3);
    }

    [Fact]
    public void CoordinateConverter_RoundtripsExactValues()
    {
        var dims = new PlotDimensions(800, 600);
        var limits = new AxisLimits(10, 50, 100, 500);
        var conv = new CoordinateConverter(dims, limits);

        double testX = 32.5;
        double testY = 275.0;

        float px = conv.GetPixelX(testX);
        float py = conv.GetPixelY(testY);

        Assert.Equal(testX, conv.GetDataX(px), 3);
        Assert.Equal(testY, conv.GetDataY(py), 3);
    }

    [Fact]
    public void TickGenerator_ProducesSensible125Steps()
    {
        var ticks = TickGenerator.Generate(0, 100, 5);
        Assert.NotEmpty(ticks);
        Assert.True(ticks.Count >= 3 && ticks.Count <= 12);
        Assert.True(ticks[0].Value >= 0);
        Assert.True(ticks[^1].Value <= 100 + 1e-4);
    }
}
