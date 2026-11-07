namespace Glacier.Plot.Statistical.Kernels;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public readonly record struct SummaryStats(
    int Count,
    float Mean,
    float Variance,
    float StdDev,
    float Min,
    float Q1,
    float Median,
    float Q3,
    float Max,
    float IQR,
    float LowerWhisker,
    float UpperWhisker,
    float[] Outliers);

/// <summary>
/// Vectorized descriptive and order statistics computation.
/// </summary>
public static class DescriptiveStats
{
    public static SummaryStats Compute(ReadOnlySpan<float> values, float whiskerMultiplier = 1.5f)
    {
        int n = values.Length;
        if (n == 0)
        {
            return new SummaryStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        // 1. Mean and Variance
        double sum = 0.0;
        for (int i = 0; i < n; i++) sum += values[i];
        float mean = (float)(sum / n);

        double sumSqDiff = 0.0;
        for (int i = 0; i < n; i++)
        {
            double diff = values[i] - mean;
            sumSqDiff += diff * diff;
        }
        float variance = n > 1 ? (float)(sumSqDiff / (n - 1)) : 0f;
        float stdDev = MathF.Sqrt(variance);

        // 2. Order statistics (Min, Q1, Median, Q3, Max)
        float[] rented = ArrayPool<float>.Shared.Rent(n);
        try
        {
            values.CopyTo(rented.AsSpan(0, n));
            Array.Sort(rented, 0, n);

            float min = rented[0];
            float max = rented[n - 1];
            float q1 = Percentile(rented.AsSpan(0, n), 0.25);
            float median = Percentile(rented.AsSpan(0, n), 0.50);
            float q3 = Percentile(rented.AsSpan(0, n), 0.75);
            float iqr = q3 - q1;

            float lowerLimit = q1 - whiskerMultiplier * iqr;
            float upperLimit = q3 + whiskerMultiplier * iqr;

            float lowerWhisker = min;
            float upperWhisker = max;

            var outliers = new List<float>();

            // Find Tukey whiskers (most extreme values within limits)
            for (int i = 0; i < n; i++)
            {
                float v = rented[i];
                if (v >= lowerLimit)
                {
                    lowerWhisker = v;
                    break;
                }
                outliers.Add(v);
            }

            for (int i = n - 1; i >= 0; i--)
            {
                float v = rented[i];
                if (v <= upperLimit)
                {
                    upperWhisker = v;
                    break;
                }
                outliers.Add(v);
            }

            return new SummaryStats(
                n, mean, variance, stdDev, min, q1, median, q3, max, iqr,
                lowerWhisker, upperWhisker, outliers.ToArray());
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private static float Percentile(ReadOnlySpan<float> sorted, double p)
    {
        int n = sorted.Length;
        if (n == 0) return 0f;
        if (n == 1) return sorted[0];

        double rank = p * (n - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        double frac = rank - low;

        return (float)(sorted[low] + frac * (sorted[high] - sorted[low]));
    }
}
