namespace Glacier.Plot.Plottables;

using System;
using Glacier.Plot.Core;
using SkiaSharp;

public enum HistogramType
{
    Count,
    Density,
    Probability
}

/// <summary>
/// Vectorized histogram frequency distribution plottable.
/// </summary>
public sealed class HistogramPlot : IPlottable
{
    private readonly float[] _binEdges;
    private readonly float[] _binCounts;
    private readonly int _binCount;
    private readonly float _dataMin;
    private readonly float _dataMax;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Emerald, IsFilled = true, FillAlpha = 180 };
    public HistogramType Type { get; set; } = HistogramType.Count;

    public HistogramPlot(ReadOnlySpan<float> values, int bins = 30, HistogramType type = HistogramType.Count, PlotStyle? style = null)
    {
        if (values.Length == 0)
            throw new ArgumentException("Input values cannot be empty.");
        if (bins <= 0)
            throw new ArgumentOutOfRangeException(nameof(bins));

        _binCount = bins;
        Type = type;
        if (style != null) Style = style;

        float min = values[0];
        float max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }

        if (max == min)
        {
            min -= 1f;
            max += 1f;
        }

        _dataMin = min;
        _dataMax = max;
        _binEdges = new float[bins + 1];
        _binCounts = new float[bins];

        float binWidth = (max - min) / bins;
        for (int i = 0; i <= bins; i++) _binEdges[i] = min + i * binWidth;

        float invBinWidth = 1.0f / binWidth;
        int totalValues = values.Length;

        for (int i = 0; i < totalValues; i++)
        {
            float v = values[i];
            int b = (int)((v - min) * invBinWidth);
            if (b < 0) b = 0;
            if (b >= bins) b = bins - 1;
            _binCounts[b]++;
        }

        if (type == HistogramType.Probability)
        {
            for (int i = 0; i < bins; i++) _binCounts[i] /= totalValues;
        }
        else if (type == HistogramType.Density)
        {
            float factor = 1.0f / (totalValues * binWidth);
            for (int i = 0; i < bins; i++) _binCounts[i] *= factor;
        }
    }

    public AxisLimits GetLimits()
    {
        float maxCount = 0f;
        for (int i = 0; i < _binCount; i++)
        {
            if (_binCounts[i] > maxCount) maxCount = _binCounts[i];
        }
        if (maxCount == 0) maxCount = 1f;

        return new AxisLimits(_dataMin, _dataMax, 0, maxCount).WithPadding(0.05, 0.1);
    }

    public void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = Style.Color.WithAlpha(Style.FillAlpha),
            IsAntialias = true
        };

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Style.Color,
            StrokeWidth = 1.0f,
            IsAntialias = true
        };

        float baselinePy = converter.GetPixelY(0.0);

        for (int i = 0; i < _binCount; i++)
        {
            float x0 = _binEdges[i];
            float x1 = _binEdges[i + 1];
            float count = _binCounts[i];

            float px0 = converter.GetPixelX(x0);
            float px1 = converter.GetPixelX(x1);
            float py = converter.GetPixelY(count);

            float top = Math.Min(py, baselinePy);
            float bottom = Math.Max(py, baselinePy);

            var rect = new SKRect(px0, top, px1, bottom);
            canvas.DrawRect(rect, fillPaint);
            canvas.DrawRect(rect, strokePaint);
        }
    }
}
