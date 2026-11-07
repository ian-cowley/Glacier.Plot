namespace Glacier.Plot.Tests;

using Glacier.Plot.Statistical.Kernels;
using Xunit;

public class RegressionTests
{
    [Fact]
    public void FitLinear_RecoversKnownSlopeAndIntercept()
    {
        // y = 2.5 * x + 4.0
        float[] x = [0f, 1f, 2f, 3f, 4f, 5f];
        float[] y = [4f, 6.5f, 9f, 11.5f, 14f, 16.5f];

        var res = RegressionKernels.FitLinear(x, y);

        Assert.Equal(2.5f, res.Slope, 3);
        Assert.Equal(4.0f, res.Intercept, 3);
        Assert.Equal(1.0f, res.RSquared, 3);
        Assert.True(res.StandardError < 1e-4f);

        Assert.Equal(19f, res.Predict(6f), 3);
    }

    [Fact]
    public void ConfidenceInterval_ExpandsAwayFromMean()
    {
        float[] x = [1f, 2f, 3f, 4f, 5f];
        float[] y = [1.1f, 1.9f, 3.2f, 3.8f, 5.1f];

        var res = RegressionKernels.FitLinear(x, y);

        var (lowCenter, highCenter) = res.ConfidenceInterval(3f); // at mean
        var (lowFar, highFar) = res.ConfidenceInterval(10f); // far away

        float widthCenter = highCenter - lowCenter;
        float widthFar = highFar - lowFar;

        Assert.True(widthFar > widthCenter, "Confidence interval envelope must flare outward away from mean.");
    }
}
