namespace Glacier.Plot.Statistical.Kernels;

using System;

public readonly record struct LinearRegressionResult(
    float Slope,
    float Intercept,
    float RSquared,
    float StandardError,
    float MeanX,
    float SumSqDiffX,
    int N)
{
    public float Predict(float x) => Slope * x + Intercept;

    public (float Lower, float Upper) ConfidenceInterval(float x, float tCritical = 1.96f)
    {
        if (N <= 2 || SumSqDiffX <= 0) return (Predict(x), Predict(x));

        float dx = x - MeanX;
        float seFit = StandardError * MathF.Sqrt((1.0f / N) + (dx * dx) / SumSqDiffX);
        float margin = tCritical * seFit;
        float yHat = Predict(x);
        return (yHat - margin, yHat + margin);
    }
}

public static class RegressionKernels
{
    public static LinearRegressionResult FitLinear(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must be identical length.");

        int n = x.Length;
        if (n < 2) return new LinearRegressionResult(0, 0, 0, 0, 0, 0, n);

        double sumX = 0.0, sumY = 0.0;
        for (int i = 0; i < n; i++)
        {
            sumX += x[i];
            sumY += y[i];
        }

        float meanX = (float)(sumX / n);
        float meanY = (float)(sumY / n);

        double sxy = 0.0;
        double sxx = 0.0;
        double syy = 0.0;

        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            sxy += dx * dy;
            sxx += dx * dx;
            syy += dy * dy;
        }

        if (sxx <= 1e-12)
        {
            return new LinearRegressionResult(0, meanY, 0, 0, meanX, (float)sxx, n);
        }

        float slope = (float)(sxy / sxx);
        float intercept = meanY - slope * meanX;

        double ssRes = 0.0;
        for (int i = 0; i < n; i++)
        {
            double yHat = slope * x[i] + intercept;
            double res = y[i] - yHat;
            ssRes += res * res;
        }

        float rSquared = syy > 1e-12 ? (float)Math.Clamp(1.0 - (ssRes / syy), 0.0, 1.0) : 0f;
        float se = n > 2 ? MathF.Sqrt((float)(ssRes / (n - 2))) : 0f;

        return new LinearRegressionResult(slope, intercept, rSquared, se, meanX, (float)sxx, n);
    }
}
