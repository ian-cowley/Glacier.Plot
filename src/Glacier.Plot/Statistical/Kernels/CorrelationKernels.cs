namespace Glacier.Plot.Statistical.Kernels;

using System;
using System.Collections.Generic;
using System.Linq;
using Glacier.Polaris;
using Glacier.Polaris.Data;

public static class CorrelationKernels
{
    public static float PearsonCorrelation(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must be identical length.");

        int n = x.Length;
        if (n <= 1) return 0f;

        double sumX = 0.0, sumY = 0.0;
        for (int i = 0; i < n; i++)
        {
            sumX += x[i];
            sumY += y[i];
        }

        double meanX = sumX / n;
        double meanY = sumY / n;

        double num = 0.0;
        double denX = 0.0;
        double denY = 0.0;

        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            num += dx * dy;
            denX += dx * dx;
            denY += dy * dy;
        }

        double denom = Math.Sqrt(denX * denY);
        return denom > 1e-12 ? (float)(num / denom) : 0f;
    }

    public static float[] CorrelationMatrix(IReadOnlyList<float[]> columns)
    {
        int p = columns.Count;
        float[] matrix = new float[p * p];

        for (int i = 0; i < p; i++)
        {
            matrix[i * p + i] = 1.0f;
            for (int j = i + 1; j < p; j++)
            {
                float r = PearsonCorrelation(columns[i], columns[j]);
                matrix[i * p + j] = r;
                matrix[j * p + i] = r;
            }
        }

        return matrix;
    }

    public static (float[] Matrix, string[] Names) ComputeCorrelationMatrix(DataFrame df, IReadOnlyList<string>? columnNames = null)
    {
        var names = new List<string>();
        var cols = new List<float[]>();

        var targets = columnNames ?? df.Columns.Select(c => c.Name).ToList();
        foreach (var name in targets)
        {
            var series = df.GetColumn(name);
            if (series is Float32Series f32)
            {
                names.Add(name);
                cols.Add(f32.Memory.ToArray());
            }
            else if (series is Float64Series f64)
            {
                names.Add(name);
                var span = f64.Memory.Span;
                float[] arr = new float[span.Length];
                for (int k = 0; k < span.Length; k++) arr[k] = (float)span[k];
                cols.Add(arr);
            }
            else if (series is Int32Series i32)
            {
                names.Add(name);
                var span = i32.Memory.Span;
                float[] arr = new float[span.Length];
                for (int k = 0; k < span.Length; k++) arr[k] = span[k];
                cols.Add(arr);
            }
        }

        float[] matrix = CorrelationMatrix(cols);
        return (matrix, names.ToArray());
    }
}
