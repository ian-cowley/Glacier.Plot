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
public sealed class BoxPlot : IPlottable
{
    private readonly float _centerX;
    private readonly float _boxWidth;
    private readonly SummaryStats _stats;

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
        var whiskerPath = new VectorPath();
        whiskerPath.AddLine(pxCenter, pyQ3, pxCenter, pyHighWhisker);
        whiskerPath.AddLine(pxCenter, pyQ1, pxCenter, pyLowWhisker);

        // Whisker crossbar caps
        float capHalfWidth = (pxRight - pxLeft) * 0.25f;
        whiskerPath.AddLine(pxCenter - capHalfWidth, pyHighWhisker, pxCenter + capHalfWidth, pyHighWhisker);
        whiskerPath.AddLine(pxCenter - capHalfWidth, pyLowWhisker, pxCenter + capHalfWidth, pyLowWhisker);
        canvas.DrawPath(whiskerPath, strokePaint);

        // 2. IQR Box
        float top = Math.Min(pyQ1, pyQ3);
        float bottom = Math.Max(pyQ1, pyQ3);
        var boxPath = new VectorPath();
        boxPath.AddRect(pxLeft, top, pxRight - pxLeft, bottom - top);
        canvas.FillPath(boxPath, fillPaint);
        canvas.DrawPath(boxPath, strokePaint);

        // 3. Median Line
        var medianPath = new VectorPath();
        medianPath.AddLine(pxLeft, pyMedian, pxRight, pyMedian);
        canvas.DrawPath(medianPath, new Paint(Colors.White, PaintStyle.Stroke, 2.5f));

        // 4. Outlier points
        if (ShowOutliers && _stats.Outliers.Length > 0)
        {
            var outlierPath = new VectorPath();
            foreach (var outVal in _stats.Outliers)
            {
                float pyOut = converter.GetPixelY(outVal);
                outlierPath.AddCircle(pxCenter, pyOut, 3.5f);
            }
            canvas.FillPath(outlierPath, new Paint(Colors.Crimson, PaintStyle.Fill));
        }
    }
}
