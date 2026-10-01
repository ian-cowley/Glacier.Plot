namespace Glacier.Plot.Plottables;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;

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

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_count == 0) return;

        float r = Style.MarkerSize * 0.5f;
        double xMin = converter.Limits.XMin;
        double xMax = converter.Limits.XMax;
        double yMin = converter.Limits.YMin;
        double yMax = converter.Limits.YMax;

        var fillPath = new VectorPath();
        var strokePath = new VectorPath();

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
                    fillPath.AddCircle(px, py, r);
                    break;

                case MarkerShape.Square:
                    fillPath.AddRect(px - r, py - r, r * 2, r * 2);
                    break;

                case MarkerShape.Diamond:
                    fillPath.MoveTo(px, py - r);
                    fillPath.LineTo(px + r, py);
                    fillPath.LineTo(px, py + r);
                    fillPath.LineTo(px - r, py);
                    fillPath.Close();
                    break;

                case MarkerShape.Cross:
                    strokePath.AddLine(px - r, py - r, px + r, py + r);
                    strokePath.AddLine(px - r, py + r, px + r, py - r);
                    break;

                case MarkerShape.Plus:
                    strokePath.AddLine(px - r, py, px + r, py);
                    strokePath.AddLine(px, py - r, px, py + r);
                    break;
            }
        }

        if (fillPath.PointCount > 0)
        {
            canvas.FillPath(fillPath, new Paint(Style.Color, PaintStyle.Fill));
        }

        if (strokePath.PointCount > 0)
        {
            canvas.DrawPath(strokePath, new Paint(Style.Color, PaintStyle.Stroke, Math.Max(1.5f, Style.StrokeWidth)));
        }
    }
}
