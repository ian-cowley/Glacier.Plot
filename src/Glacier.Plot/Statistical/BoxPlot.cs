namespace Glacier.Plot.Statistical;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Classical Tukey box-and-whisker plot displaying medians, IQR boxes, whiskers, and outliers.
/// </summary>
public sealed class BoxPlot : IPlottable, IDisposable
{
    private readonly float _centerX;
    private readonly float _boxWidth;
    private readonly SummaryStats _stats;

    private readonly VectorPath _whiskerPath = new();
    private readonly VectorPath _boxPath = new();
    private readonly VectorPath _medianPath = new();
    private readonly VectorPath _outlierPath = new();

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.SteelBlue, FillAlpha = 180 };
    public bool ShowOutliers { get; set; } = true;

    public BoxPlot(float centerX, ReadOnlySpan<float> samples, float boxWidth = 0.6f, float whiskerMultiplier = 1.5f, PlotStyle? style = null)
    {
        _centerX = centerX;
        _boxWidth = boxWidth;
        _stats = DescriptiveStats.Compute(samples, whiskerMultiplier);
        if (style != null) Style = style;
    }

    public AxisLimits GetLimits()
    {
        float minX = _centerX - _boxWidth * 0.5f;
        float maxX = _centerX + _boxWidth * 0.5f;
        float minY = _stats.Min;
        float maxY = _stats.Max;

        return new AxisLimits(minX, maxX, minY, maxY).WithPadding(0.05, 0.05);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_stats.Count == 0) return;

        float pxCenter = converter.GetPixelX(_centerX);
        float pxLeft = converter.GetPixelX(_centerX - _boxWidth * 0.5f);
        float pxRight = converter.GetPixelX(_centerX + _boxWidth * 0.5f);

        float pyQ1 = converter.GetPixelY(_stats.Q1);
        float pyQ3 = converter.GetPixelY(_stats.Q3);
        float pyMedian = converter.GetPixelY(_stats.Median);
        float pyLowWhisker = converter.GetPixelY(_stats.LowerWhisker);
        float pyHighWhisker = converter.GetPixelY(_stats.UpperWhisker);

        var strokePaint = new Paint(Style.Color, PaintStyle.Stroke, 1.5f);
        var fillPaint = new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill);

        // 1. Whisker lines
        _whiskerPath.Clear();
        _whiskerPath.AddLine(pxCenter, pyQ3, pxCenter, pyHighWhisker);
        _whiskerPath.AddLine(pxCenter, pyQ1, pxCenter, pyLowWhisker);

        // Whisker crossbar caps
        float capHalfWidth = (pxRight - pxLeft) * 0.25f;
        _whiskerPath.AddLine(pxCenter - capHalfWidth, pyHighWhisker, pxCenter + capHalfWidth, pyHighWhisker);
        _whiskerPath.AddLine(pxCenter - capHalfWidth, pyLowWhisker, pxCenter + capHalfWidth, pyLowWhisker);
        canvas.DrawPath(_whiskerPath, strokePaint);

        // 2. IQR Box
        float top = Math.Min(pyQ1, pyQ3);
        float bottom = Math.Max(pyQ1, pyQ3);
        _boxPath.Clear();
        _boxPath.AddRect(pxLeft, top, pxRight - pxLeft, bottom - top);
        canvas.FillPath(_boxPath, fillPaint);
        canvas.DrawPath(_boxPath, strokePaint);

        // 3. Median Line
        _medianPath.Clear();
        _medianPath.AddLine(pxLeft, pyMedian, pxRight, pyMedian);
        canvas.DrawPath(_medianPath, new Paint(Colors.White, PaintStyle.Stroke, 2.5f));

        // 4. Outlier points
        if (ShowOutliers && _stats.Outliers.Length > 0)
        {
            _outlierPath.Clear();
            foreach (var outVal in _stats.Outliers)
            {
                float pyOut = converter.GetPixelY(outVal);
                _outlierPath.AddCircle(pxCenter, pyOut, 3.5f);
            }
            canvas.FillPath(_outlierPath, new Paint(Colors.Crimson, PaintStyle.Fill));
        }
    }

    public void Dispose()
    {
        _whiskerPath.Clear();
        _boxPath.Clear();
        _medianPath.Clear();
        _outlierPath.Clear();
    }
}
