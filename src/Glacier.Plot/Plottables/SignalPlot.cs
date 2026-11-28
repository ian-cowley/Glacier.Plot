namespace Glacier.Plot.Plottables;

using System;
using System.Buffers;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;

/// <summary>
/// Ultra-high-speed continuous line plot designed for datasets from tens of points to tens of millions of points.
/// Automatically applies SIMD LTTB / MinMax downsampling directly to VectorPath without intermediate point allocations.
/// </summary>
public sealed class SignalPlot : IPlottable, IDisposable
{
    private readonly ReadOnlyMemory<float> _xMemory;
    private readonly ReadOnlyMemory<float> _yMemory;
    private readonly int _count;
    private readonly bool _isUniform;
    private readonly float _xStart;
    private readonly float _xStep;
    private AxisLimits _cachedLimits = AxisLimits.Empty;

    // Reusable VectorPath instances
    private readonly VectorPath _path = new();
    private readonly VectorPath _fillPath = new();
    private readonly VectorPath _markerPath = new();

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new();
    public DecimationStrategy Decimation { get; set; } = DecimationStrategy.Auto;

    /// <summary>
    /// Creates a signal plot with explicit X and Y coordinates via ReadOnlyMemory without heap array duplication.
    /// </summary>
    public SignalPlot(ReadOnlyMemory<float> x, ReadOnlyMemory<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y memories must have identical length.");

        _count = x.Length;
        _xMemory = x;
        _yMemory = y;
        _isUniform = false;
        if (style != null) Style = style;
    }

    /// <summary>
    /// Creates a signal plot with uniformly spaced Y coordinates via ReadOnlyMemory (X = xStart + i * xStep).
    /// </summary>
    public SignalPlot(ReadOnlyMemory<float> y, float xStart = 0f, float xStep = 1f, PlotStyle? style = null)
    {
        _count = y.Length;
        _yMemory = y;
        _isUniform = true;
        _xStart = xStart;
        _xStep = xStep;
        if (style != null) Style = style;
    }

    /// <summary>
    /// Creates a signal plot with explicit X and Y arrays without duplication.
    /// </summary>
    public SignalPlot(float[] x, float[] y, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)x, (ReadOnlyMemory<float>)y, style)
    {
    }

    /// <summary>
    /// Creates a signal plot with uniformly spaced Y array.
    /// </summary>
    public SignalPlot(float[] y, float xStart = 0f, float xStep = 1f, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)y, xStart, xStep, style)
    {
    }

    /// <summary>
    /// Creates a signal plot with explicit X and Y coordinates.
    /// </summary>
    public SignalPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)x.ToArray(), (ReadOnlyMemory<float>)y.ToArray(), style)
    {
    }

    /// <summary>
    /// Creates a signal plot with uniformly spaced Y coordinates (X = xStart + i * xStep).
    /// Eliminates memory allocation for X coordinates.
    /// </summary>
    public SignalPlot(ReadOnlySpan<float> y, float xStart = 0f, float xStep = 1f, PlotStyle? style = null)
        : this((ReadOnlyMemory<float>)y.ToArray(), xStart, xStep, style)
    {
    }

    public AxisLimits GetLimits()
    {
        if (_cachedLimits.IsValid) return _cachedLimits;

        if (_count == 0)
        {
            _cachedLimits = AxisLimits.Default;
            return _cachedLimits;
        }

        if (_isUniform)
        {
            _cachedLimits = AxisLimits.FromDataUniform(_yMemory.Span[.._count], _xStart, _xStep);
        }
        else
        {
            _cachedLimits = AxisLimits.FromData(_xMemory.Span[.._count], _yMemory.Span[.._count]);
        }

        return _cachedLimits;
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_count < 2) return;

        int targetPixels = Math.Max(100, (int)Math.Ceiling(converter.Dimensions.DataWidth));
        bool shouldDecimate = Decimation != DecimationStrategy.None && _count > targetPixels * 2;
        bool useMinMax = shouldDecimate &&
                         (Decimation == DecimationStrategy.MinMax ||
                          (Decimation == DecimationStrategy.Auto && _count > targetPixels * 50));

        double baselineDataY = Math.Max(0.0, converter.Limits.YMin);
        float baselinePy = converter.GetPixelY(baselineDataY);

        _path.Clear();
        _fillPath.Clear();

        if (shouldDecimate)
        {
            if (useMinMax)
            {
                VectorPath? fillTarget = Style.IsFilled ? _fillPath : null;
                if (_isUniform)
                    MinMaxKernels.DownsampleUniformToPath(_yMemory.Span[.._count], _xStart, _xStep, targetPixels, converter, _path, fillTarget, baselineDataY);
                else
                    MinMaxKernels.DownsampleToPath(_xMemory.Span[.._count], _yMemory.Span[.._count], targetPixels, converter, _path, fillTarget, baselineDataY);

                if (Style.IsFilled && _fillPath.PointCount >= 2)
                {
                    float firstPx = _fillPath.Points[0].X;
                    float lastPx = _fillPath.Points[^1].X;
                    _fillPath.LineTo(lastPx, baselinePy);
                    _fillPath.LineTo(firstPx, baselinePy);
                    _fillPath.Close();
                    canvas.FillPath(_fillPath, new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill));
                }
            }
            else
            {
                if (_isUniform)
                    LttbKernels.DownsampleUniformToPath(_yMemory.Span[.._count], _xStart, _xStep, targetPixels, converter, _path);
                else
                    LttbKernels.DownsampleToPath(_xMemory.Span[.._count], _yMemory.Span[.._count], targetPixels, converter, _path);
            }
        }
        else
        {
            var ySpan = _yMemory.Span[.._count];
            if (_isUniform)
            {
                if (_count >= 256)
                {
                    float[] xData = ArrayPool<float>.Shared.Rent(_count);
                    float[] pxBuf = ArrayPool<float>.Shared.Rent(_count);
                    float[] pyBuf = ArrayPool<float>.Shared.Rent(_count);
                    try
                    {
                        for (int i = 0; i < _count; i++) xData[i] = _xStart + i * _xStep;
                        converter.TransformCoordinates(xData.AsSpan(0, _count), ySpan, pxBuf.AsSpan(0, _count), pyBuf.AsSpan(0, _count));
                        _path.MoveTo(pxBuf[0], pyBuf[0]);
                        for (int i = 1; i < _count; i++) _path.LineTo(pxBuf[i], pyBuf[i]);
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(xData);
                        ArrayPool<float>.Shared.Return(pxBuf);
                        ArrayPool<float>.Shared.Return(pyBuf);
                    }
                }
                else
                {
                    _path.MoveTo(converter.GetPixelX(_xStart), converter.GetPixelY(ySpan[0]));
                    for (int i = 1; i < _count; i++)
                    {
                        _path.LineTo(converter.GetPixelX(_xStart + i * _xStep), converter.GetPixelY(ySpan[i]));
                    }
                }
            }
            else
            {
                var xSpan = _xMemory.Span[.._count];
                if (_count >= 256)
                {
                    float[] pxBuf = ArrayPool<float>.Shared.Rent(_count);
                    float[] pyBuf = ArrayPool<float>.Shared.Rent(_count);
                    try
                    {
                        converter.TransformCoordinates(xSpan, ySpan, pxBuf.AsSpan(0, _count), pyBuf.AsSpan(0, _count));
                        _path.MoveTo(pxBuf[0], pyBuf[0]);
                        for (int i = 1; i < _count; i++) _path.LineTo(pxBuf[i], pyBuf[i]);
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(pxBuf);
                        ArrayPool<float>.Shared.Return(pyBuf);
                    }
                }
                else
                {
                    _path.MoveTo(converter.GetPixelX(xSpan[0]), converter.GetPixelY(ySpan[0]));
                    for (int i = 1; i < _count; i++)
                    {
                        _path.LineTo(converter.GetPixelX(xSpan[i]), converter.GetPixelY(ySpan[i]));
                    }
                }
            }
        }

        // Fill under curve if enabled (non-MinMax path: contiguous polyline in _path)
        if (Style.IsFilled && !useMinMax && _path.PointCount >= 2)
        {
            _fillPath.AddPath(_path);

            float lastX = _isUniform ? _xStart + (_count - 1) * _xStep : _xMemory.Span[_count - 1];
            float firstX = _isUniform ? _xStart : _xMemory.Span[0];

            float lastPx = converter.GetPixelX(lastX);
            float firstPx = converter.GetPixelX(firstX);

            _fillPath.LineTo(lastPx, baselinePy);
            _fillPath.LineTo(firstPx, baselinePy);
            _fillPath.Close();

            canvas.FillPath(_fillPath, new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill));
        }

        // Stroke line
        canvas.DrawPath(_path, new Paint(
            Style.Color,
            PaintStyle.Stroke,
            Style.StrokeWidth,
            StrokeJoin.Round,
            StrokeCap.Round));

        // Optional markers (for small point sets)
        if (Style.Marker != MarkerShape.None && _count <= 500)
        {
            _markerPath.Clear();
            float r = Style.MarkerSize * 0.5f;
            var ySpan = _yMemory.Span;

            for (int i = 0; i < _count; i++)
            {
                float xVal = _isUniform ? _xStart + i * _xStep : _xMemory.Span[i];
                float px = converter.GetPixelX(xVal);
                float py = converter.GetPixelY(ySpan[i]);

                if (Style.Marker == MarkerShape.Circle)
                    _markerPath.AddCircle(px, py, r);
                else if (Style.Marker == MarkerShape.Square)
                    _markerPath.AddRect(px - r, py - r, r * 2, r * 2);
            }

            if (_markerPath.PointCount > 0)
            {
                canvas.FillPath(_markerPath, new Paint(Style.Color, PaintStyle.Fill));
            }
        }
    }

    public void Dispose()
    {
        _path.Clear();
        _fillPath.Clear();
        _markerPath.Clear();
    }
}
