namespace Glacier.Plot.Plottables;

using System;
using Glacier.Plot.Core;
using SkiaSharp;

/// <summary>
/// High-speed batched scatter plot with viewport bounding box culling.
/// </summary>
public sealed class ScatterPlot : IPlottable
{
    private readonly float[] _xValues;
    private readonly float[] _yValues;
    private readonly int _count;
    private AxisLimits _cachedLimits = AxisLimits.Empty;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Marker = MarkerShape.Circle, MarkerSize = 6.0f };

    public ScatterPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        _count = x.Length;
        _xValues = x.ToArray();
        _yValues = y.ToArray();
        if (style != null) Style = style;
        if (Style.Marker == MarkerShape.None) Style.Marker = MarkerShape.Circle;
    }

    public AxisLimits GetLimits()
    {
        if (_cachedLimits.IsValid) return _cachedLimits;
        if (_count == 0) return AxisLimits.Default;

        _cachedLimits = AxisLimits.FromData(_xValues.AsSpan(0, _count), _yValues.AsSpan(0, _count));
        return _cachedLimits;
    }

    public void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_count == 0) return;

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = Style.Color,
            IsAntialias = true
        };

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = Style.Color,
            StrokeWidth = Math.Max(1.5f, Style.StrokeWidth),
            IsAntialias = true
        };

        float r = Style.MarkerSize * 0.5f;
        double xMin = converter.Limits.XMin;
        double xMax = converter.Limits.XMax;
        double yMin = converter.Limits.YMin;
        double yMax = converter.Limits.YMax;

        for (int i = 0; i < _count; i++)
        {
            float x = _xValues[i];
            float y = _yValues[i];

            // Viewport culling
            if (x < xMin || x > xMax || y < yMin || y > yMax) continue;

            float px = converter.GetPixelX(x);
            float py = converter.GetPixelY(y);

            switch (Style.Marker)
            {
                case MarkerShape.Circle:
                    canvas.DrawCircle(px, py, r, fillPaint);
                    break;

                case MarkerShape.Square:
                    canvas.DrawRect(px - r, py - r, r * 2, r * 2, fillPaint);
                    break;

                case MarkerShape.Diamond:
                    using (var diamond = new SKPath())
                    {
                        diamond.MoveTo(px, py - r);
                        diamond.LineTo(px + r, py);
                        diamond.LineTo(px, py + r);
                        diamond.LineTo(px - r, py);
                        diamond.Close();
                        canvas.DrawPath(diamond, fillPaint);
                    }
                    break;

                case MarkerShape.Cross:
                    canvas.DrawLine(px - r, py - r, px + r, py + r, strokePaint);
                    canvas.DrawLine(px - r, py + r, px + r, py - r, strokePaint);
                    break;

                case MarkerShape.Plus:
                    canvas.DrawLine(px - r, py, px + r, py, strokePaint);
                    canvas.DrawLine(px, py - r, px, py + r, strokePaint);
                    break;
            }
        }
    }
}
