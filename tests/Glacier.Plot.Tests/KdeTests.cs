namespace Glacier.Plot.Tests;

using System;
using Glacier.Plot.Statistical.Kernels;
using Xunit;

public class KdeTests
{
    [Fact]
    public void FastExpScalar_AccurateAgainstMathExp()
    {
        for (float x = -20f; x <= 0f; x += 0.5f)
        {
            float approx = KdeKernels.FastExpScalar(x);
            float actual = MathF.Exp(x);
            float diff = MathF.Abs(approx - actual);
            Assert.True(diff < 1e-4f, $"Exp mismatch at {x}: approx={approx}, actual={actual}");
        }
    }

    [Fact]
    public void Kde_NormalDistribution_PeaksAtMean()
    {
        // Sample normal distribution centered at 0 with std=1
        int n = 1000;
        float[] samples = new float[n];
        var rand = new Random(42);
        for (int i = 0; i < n; i += 2)
        {
            // Box-Muller transform
            double u1 = 1.0 - rand.NextDouble();
            double u2 = 1.0 - rand.NextDouble();
            double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double z1 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            samples[i] = (float)z0;
            if (i + 1 < n) samples[i + 1] = (float)z1;
        }

        float[] grid = [-3f, -2f, -1f, 0f, 1f, 2f, 3f];
        float[] density = new float[grid.Length];

        KdeKernels.VectorizedKde(samples, grid, bandwidth: 0.3f, density);

        // Peak should be at grid[3] (0.0)
        float maxD = density[0];
        int maxIdx = 0;
        for (int i = 1; i < density.Length; i++)
        {
            if (density[i] > maxD)
            {
                maxD = density[i];
                maxIdx = i;
            }
        }

        Assert.Equal(3, maxIdx);
        // Density at 0 should be around 1 / sqrt(2 * pi) ~ 0.3989
        Assert.True(density[3] > 0.3f && density[3] < 0.5f);
        // Tails should be much lower
        Assert.True(density[0] < density[3] * 0.1f);
        Assert.True(density[^1] < density[3] * 0.1f);
    }

    [Fact]
    public void Bandwidth_Selectors_ComputeValidValues()
    {
        float[] samples = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f];
        float silverman = KdeKernels.SilvermanBandwidth(samples, 3f, 5f);
        float scott = KdeKernels.ScottBandwidth(samples.Length, 3f);

        Assert.True(silverman > 0f);
        Assert.True(scott > 0f);
    }
}
