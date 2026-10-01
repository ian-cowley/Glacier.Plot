namespace Glacier.Plot.Plottables;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;

/// <summary>
/// Bar chart plottable supporting numeric positions or categorical series.
/// </summary>
public sealed class BarPlot : IPlottable
{
    private readonly float[] _positions;
    private readonly float[] _values;
    private readonly string[]? _categoryLabels;
    private readonly float _barWidth;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.SteelBlue };
    public float Baseline { get; set; } = 0.0f;

    public BarPlot(ReadOnlySpan<float> positions, ReadOnlySpan<float> values, float barWidth = 0.8f, PlotStyle? style = null)
    {
        if (positions.Length != values.Length)
            throw new ArgumentException("Positions and values must have equal length.");

        _positions = positions.ToArray();
        _values = values.ToArray();
        _barWidth = barWidth;
        if (style != null) Style = style;
    }

    public BarPlot(ReadOnlySpan<string> categories, ReadOnlySpan<float> values, float barWidth = 0.8f, PlotStyle? style = null)
    {
        if (categories.Length != values.Length)
            throw new ArgumentException("Categories and values must have equal length.");

        _categoryLabels = categories.ToArray();
        _values = values.ToArray();
        _positions = new float[values.Length];
        for (int i = 0; i < values.Length; i++) _positions[i] = i;
        _barWidth = barWidth;
        if (style != null) Style = style;
    }

    public AxisLimits GetLimits()
    {
        if (_values.Length == 0) return AxisLimits.Default;

        float minX = _positions[0] - _barWidth * 0.5f;
        float maxX = _positions[_positions.Length - 1] + _barWidth * 0.5f;
        float minY = Baseline;
        float maxY = Baseline;

        foreach (var v in _values)
        {
            if (v < minY) minY = v;
            if (v > maxY) maxY = v;
        }

        if (maxY == minY) maxY += 1f;

        return new AxisLimits(minX, maxX, minY, maxY).WithPadding(0.05, 0.1);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_values.Length == 0) return;

        var fillPaint = new Paint(Style.Color, PaintStyle.Fill);
        var strokePaint = new Paint(Style.Color.WithAlpha(200), PaintStyle.Stroke, 1.0f);

        float halfWidth = _barWidth * 0.5f;
        float baselinePy = converter.GetPixelY(Baseline);

        for (int i = 0; i < _values.Length; i++)
        {
            float pos = _positions[i];
            float val = _values[i];

            float pxLeft = converter.GetPixelX(pos - halfWidth);
            float pxRight = converter.GetPixelX(pos + halfWidth);
            float pyVal = converter.GetPixelY(val);

            float top = Math.Min(pyVal, baselinePy);
            float bottom = Math.Max(pyVal, baselinePy);

            var rectPath = new VectorPath();
            rectPath.AddRect(pxLeft, top, pxRight - pxLeft, bottom - top);
            canvas.FillPath(rectPath, fillPaint);
            canvas.DrawPath(rectPath, strokePaint);
        }
    }
}
