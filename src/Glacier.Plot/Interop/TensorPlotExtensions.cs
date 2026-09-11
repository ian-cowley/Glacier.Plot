namespace Glacier.Plot.Interop;

using System;
using Glacier.Plot.Core;
using Glacier.Plot.Figures;
using Glacier.Plot.Plottables;
using Glacier.Tensor.Core;
using SkiaSharp;

/// <summary>
/// Plotting extensions for Glacier.Tensor N-dimensional strided tensors.
/// </summary>
public static class TensorPlotExtensions
{
    public static SignalPlot PlotLine(this Figure fig, Tensor<float> tensor, string? label = null, SKColor? color = null)
    {
        return fig.PlotSignal(tensor.AsSpan(), 0f, 1f, label, color);
    }

    public static HistogramPlot PlotHistogram(this Figure fig, Tensor<float> tensor, int bins = 30, string? label = null, SKColor? color = null)
    {
        return fig.PlotHistogram(tensor.AsSpan(), bins, label, color);
    }

    public static HeatmapPlot PlotHeatmap(this Figure fig, Tensor<float> matrix, ColorMap map = ColorMap.Viridis)
    {
        if (matrix.Rank != 2)
            throw new ArgumentException($"Heatmap requires a 2D rank-2 tensor, but got rank {matrix.Rank}.");

        int rows = matrix.Shape[0];
        int cols = matrix.Shape[1];
        return fig.PlotHeatmap(matrix.AsSpan(), rows, cols, map);
    }
}
