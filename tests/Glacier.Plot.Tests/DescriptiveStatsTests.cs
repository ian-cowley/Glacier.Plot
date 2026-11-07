namespace Glacier.Plot.Tests;

using Glacier.Plot.Statistical.Kernels;
using Xunit;

public class DescriptiveStatsTests
{
    [Fact]
    public void Compute_CalculatesExactStats()
    {
        float[] data = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f];
        var stats = DescriptiveStats.Compute(data);

        Assert.Equal(10, stats.Count);
        Assert.Equal(5.5f, stats.Mean, 3);
        Assert.Equal(1.0f, stats.Min);
        Assert.Equal(10.0f, stats.Max);
        Assert.Equal(5.5f, stats.Median, 3);
        Assert.True(stats.IQR > 0f);
        Assert.Empty(stats.Outliers);
    }

    [Fact]
    public void Compute_DetectsTukeyOutliers()
    {
        float[] data = [10f, 11f, 10.5f, 9.5f, 10.2f, 10.8f, 9.8f, 10.1f, 100f, -50f];
        var stats = DescriptiveStats.Compute(data);

        Assert.NotEmpty(stats.Outliers);
        Assert.Contains(100f, stats.Outliers);
        Assert.Contains(-50f, stats.Outliers);
    }

    [Fact]
    public void Compute_SingleElement_ReturnsValid()
    {
        float[] data = [42f];
        var stats = DescriptiveStats.Compute(data);

        Assert.Equal(1, stats.Count);
        Assert.Equal(42f, stats.Mean);
        Assert.Equal(42f, stats.Median);
        Assert.Equal(0f, stats.Variance);
        Assert.Equal(0f, stats.IQR);
    }

    [Fact]
    public void Compute_Empty_ReturnsEmptySummary()
    {
        var stats = DescriptiveStats.Compute([]);
        Assert.Equal(0, stats.Count);
    }
}
