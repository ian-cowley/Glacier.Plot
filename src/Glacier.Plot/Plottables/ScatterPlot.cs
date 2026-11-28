namespace Glacier.Plot.Plottables;

using System;
using System.Buffers;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;

/// <summary>
/// High-speed batched scatter plot with viewport bounding box culling.
/// </summary>
public sealed class ScatterPlot : IPlottable, IDisposable
{
    private readonly ReadOnlyMemory<float> _xMemory;
    private readonly ReadOnlyMemory<float> _yMemory;
    private readonly int _count;
    private AxisLimits _cachedLimits = AxisLimits.Empty;

    private readonly VectorPath _fillPath = new();
    private readonly VectorPath _strokePath = new();

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Marker = MarkerShape.Circle, MarkerSize = 6.0f };

    /// <summary>
    /// Creates a scatter plot backed by ReadOnlyMemory without copying underlying arrays.
    /// </summary>
    public ScatterPlot(ReadOnlyMemory<float> x, ReadOnlyMemory<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y memories must have identical length.");

        _count = x.Length;
        _xMemory = x;
        _yMemory = y;
        if (style != null) Style = style;
        if (Style.Marker == MarkerShape.None) Style.Marker = MarkerShape.Circle;
    }

    public ScatterPlot(float[] x, float[] y, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)x, (ReadOnlyMemory<float>)y, style)
    {
    }

    public ScatterPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)x.ToArray(), (ReadOnlyMemory<float>)y.ToArray(), style)
    {
    }

    public AxisLimits GetLimits()
    {
        if (_cachedLimits.IsValid) return _cachedLimits;
        if (_count == 0) return AxisLimits.Default;

        _cachedLimits = AxisLimits.FromData(_xMemory.Span[.._count], _yMemory.Span[.._count]);
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

        _fillPath.Clear();
        _strokePath.Clear();

        var xSpan = _xMemory.Span[.._count];
        var ySpan = _yMemory.Span[.._count];

        if (_count >= 256)
        {
            float[] pxBuf = ArrayPool<float>.Shared.Rent(_count);
            float[] pyBuf = ArrayPool<float>.Shared.Rent(_count);
            try
            {
                converter.TransformCoordinates(xSpan, ySpan, pxBuf.AsSpan(0, _count), pyBuf.AsSpan(0, _count));
                for (int i = 0; i < _count; i++)
                {
                    float x = xSpan[i];
                    float y = ySpan[i];
                    if (x < xMin || x > xMax || y < yMin || y > yMax) continue;

                    AddMarkerGeometry(pxBuf[i], pyBuf[i], r);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(pxBuf);
                ArrayPool<float>.Shared.Return(pyBuf);
            }
        }
        else
        {
            for (int i = 0; i < _count; i++)
            {
                float x = xSpan[i];
                float y = ySpan[i];

                // Viewport culling
                if (x < xMin || x > xMax || y < yMin || y > yMax) continue;

                float px = converter.GetPixelX(x);
                float py = converter.GetPixelY(y);
                AddMarkerGeometry(px, py, r);
            }
        }

        if (_fillPath.PointCount > 0)
        {
            canvas.FillPath(_fillPath, new Paint(Style.Color, PaintStyle.Fill));
        }

        if (_strokePath.PointCount > 0)
        {
            canvas.DrawPath(_strokePath, new Paint(Style.Color, PaintStyle.Stroke, Math.Max(1.5f, Style.StrokeWidth)));
        }
    }

    private void AddMarkerGeometry(float px, float py, float r)
    {
        switch (Style.Marker)
        {
            case MarkerShape.Circle:
                _fillPath.AddCircle(px, py, r);
                break;

            case MarkerShape.Square:
                _fillPath.AddRect(px - r, py - r, r * 2, r * 2);
                break;

            case MarkerShape.Diamond:
                _fillPath.MoveTo(px, py - r);
                _fillPath.LineTo(px + r, py);
                _fillPath.LineTo(px, py + r);
                _fillPath.LineTo(px - r, py);
                _fillPath.Close();
                break;

            case MarkerShape.Cross:
                _strokePath.AddLine(px - r, py - r, px + r, py + r);
                _strokePath.AddLine(px - r, py + r, px + r, py - r);
                break;

            case MarkerShape.Plus:
                _strokePath.AddLine(px - r, py, px + r, py);
                _strokePath.AddLine(px, py - r, px, py + r);
                break;
        }
    }

    public void Dispose()
    {
        _fillPath.Clear();
        _strokePath.Clear();
    }
}
