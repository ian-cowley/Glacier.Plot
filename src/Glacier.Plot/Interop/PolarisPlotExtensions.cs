namespace Glacier.Plot.Interop;

using System;
using Glacier.Plot.Core;
using Glacier.Plot.Figures;
using Glacier.Plot.Plottables;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using SkiaSharp;

/// <summary>
/// Zero-copy plotting extensions for Glacier.Polaris DataFrames and Series.
/// </summary>
public static class PolarisPlotExtensions
{
    /// <summary>
    /// Extracts a float span from an ISeries without copying when the underlying type is float.
    /// </summary>
    public static float[] ToFloatArray(this ISeries series)
    {
        if (series is Float32Series f32)
        {
            return f32.Memory.ToArray();
        }

        float[] arr = new float[series.Length];
        if (series is Float64Series f64)
        {
            var span = f64.Memory.Span;
            for (int i = 0; i < span.Length; i++) arr[i] = (float)span[i];
        }
        else if (series is Int32Series i32)
        {
            var span = i32.Memory.Span;
            for (int i = 0; i < span.Length; i++) arr[i] = span[i];
        }
        else
        {
            for (int i = 0; i < series.Length; i++)
            {
                object? obj = series.Get(i);
                arr[i] = obj != null ? Convert.ToSingle(obj) : 0f;
            }
        }
        return arr;
    }

    /// <summary>
    /// Returns a ReadOnlyMemory&lt;float&gt; from an ISeries with zero-copy when the underlying series is Float32Series.
    /// </summary>
    public static ReadOnlyMemory<float> AsFloatMemory(this ISeries series)
    {
        if (series is Float32Series f32)
        {
            return f32.Memory;
        }
        return ToFloatArray(series);
    }

    public static SignalPlot PlotLine(this Figure fig, ISeries xSeries, ISeries ySeries, string? label = null, SKColor? color = null)
    {
        var x = xSeries.AsFloatMemory();
        var y = ySeries.AsFloatMemory();
        return fig.PlotLine(x, y, label ?? ySeries.Name, color);
    }

    public static ScatterPlot PlotScatter(this Figure fig, ISeries xSeries, ISeries ySeries, string? label = null, SKColor? color = null)
    {
        float[] x = xSeries.ToFloatArray();
        float[] y = ySeries.ToFloatArray();
        return fig.PlotScatter(x, y, label ?? ySeries.Name, color);
    }

    public static HistogramPlot PlotHistogram(this Figure fig, ISeries series, int bins = 30, string? label = null, SKColor? color = null)
    {
        float[] vals = series.ToFloatArray();
        return fig.PlotHistogram(vals, bins, label ?? series.Name, color);
    }

    public static Figure PlotLine(this DataFrame df, string xColumn, string yColumn, string? title = null)
    {
        var fig = new Figure { Title = title ?? $"{yColumn} vs {xColumn}" };
        fig.XAxis.Label = xColumn;
        fig.YAxis.Label = yColumn;
        fig.PlotLine(df.GetColumn(xColumn), df.GetColumn(yColumn), yColumn);
        return fig;
    }

    public static Figure PlotScatter(this DataFrame df, string xColumn, string yColumn, string? title = null)
    {
        var fig = new Figure { Title = title ?? $"{yColumn} vs {xColumn}" };
        fig.XAxis.Label = xColumn;
        fig.YAxis.Label = yColumn;
        fig.PlotScatter(df.GetColumn(xColumn), df.GetColumn(yColumn), yColumn);
        return fig;
    }

    public static Figure PlotHistogram(this DataFrame df, string column, int bins = 30, string? title = null)
    {
        var fig = new Figure { Title = title ?? $"Distribution of {column}" };
        fig.XAxis.Label = column;
        fig.YAxis.Label = "Count";
        fig.PlotHistogram(df.GetColumn(column), bins, column);
        return fig;
    }
}
