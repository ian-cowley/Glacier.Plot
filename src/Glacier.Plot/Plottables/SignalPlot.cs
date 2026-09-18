namespace Glacier.Plot.Plottables;

using System;
using System.Buffers;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using SkiaSharp;

/// <summary>
/// Ultra-high-speed continuous line plot designed for datasets from tens of points to tens of millions of points.
/// Automatically applies SIMD LTTB / MinMax downsampling when points exceed screen resolution.
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

    // Reusable SKPath and SKPaint fields
    private readonly SKPath _path = new();
    private readonly SKPath _fillPath = new();
    private readonly SKPaint _fillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _strokePaint = new()
    {
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };
    private readonly SKPaint _markerPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };

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

    public void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_count < 2) return;

        int targetPixels = Math.Max(100, (int)Math.Ceiling(converter.Dimensions.DataWidth));
        bool shouldDecimate = Decimation != DecimationStrategy.None && _count > targetPixels * 2;

        float[]? rentedX = null;
        float[]? rentedY = null;

        ReadOnlySpan<float> renderX;
        ReadOnlySpan<float> renderY;
        int renderCount;

        if (shouldDecimate)
        {
            int bufferSize = Math.Max(targetPixels * 2, 2048);
            rentedX = ArrayPool<float>.Shared.Rent(bufferSize);
            rentedY = ArrayPool<float>.Shared.Rent(bufferSize);

            bool useMinMax = Decimation == DecimationStrategy.MinMax ||
                             (Decimation == DecimationStrategy.Auto && _count > targetPixels * 50);

            if (useMinMax)
            {
                renderCount = _isUniform
                    ? MinMaxKernels.DownsampleUniform(_yMemory.Span[.._count], _xStart, _xStep, targetPixels, rentedX, rentedY)
                    : MinMaxKernels.Downsample(_xMemory.Span[.._count], _yMemory.Span[.._count], targetPixels, rentedX, rentedY);
            }
            else
            {
                renderCount = _isUniform
                    ? LttbKernels.DownsampleUniform(_yMemory.Span[.._count], _xStart, _xStep, targetPixels, rentedX, rentedY)
                    : LttbKernels.Downsample(_xMemory.Span[.._count], _yMemory.Span[.._count], targetPixels, rentedX, rentedY);
            }

            renderX = rentedX.AsSpan(0, renderCount);
            renderY = rentedY.AsSpan(0, renderCount);
        }
        else
        {
            renderCount = _count;
            if (_isUniform)
            {
                rentedX = ArrayPool<float>.Shared.Rent(_count);
                for (int i = 0; i < _count; i++) rentedX[i] = _xStart + i * _xStep;
                renderX = rentedX.AsSpan(0, _count);
            }
            else
            {
                renderX = _xMemory.Span[.._count];
            }
            renderY = _yMemory.Span[.._count];
        }

        try
        {
            _path.Rewind();
            float firstPx = converter.GetPixelX(renderX[0]);
            float firstPy = converter.GetPixelY(renderY[0]);
            _path.MoveTo(firstPx, firstPy);

            for (int i = 1; i < renderCount; i++)
            {
                float px = converter.GetPixelX(renderX[i]);
                float py = converter.GetPixelY(renderY[i]);
                _path.LineTo(px, py);
            }

            // Fill under curve if enabled
            if (Style.IsFilled)
            {
                _fillPath.Rewind();
                _fillPath.AddPath(_path);
                float lastPx = converter.GetPixelX(renderX[renderCount - 1]);
                float baselinePy = converter.GetPixelY(Math.Max(0, converter.Limits.YMin));
                _fillPath.LineTo(lastPx, baselinePy);
                _fillPath.LineTo(firstPx, baselinePy);
                _fillPath.Close();

                _fillPaint.Color = Style.Color.WithAlpha(Style.FillAlpha);
                canvas.DrawPath(_fillPath, _fillPaint);
            }

            // Stroke line
            _strokePaint.Color = Style.Color;
            _strokePaint.StrokeWidth = Style.StrokeWidth;

            if (Style.Pattern == LinePattern.Dashed)
                _strokePaint.PathEffect = SKPathEffect.CreateDash([10f, 6f], 0f);
            else if (Style.Pattern == LinePattern.Dotted)
                _strokePaint.PathEffect = SKPathEffect.CreateDash([2f, 4f], 0f);
            else if (Style.Pattern == LinePattern.DashDot)
                _strokePaint.PathEffect = SKPathEffect.CreateDash([10f, 4f, 2f, 4f], 0f);
            else
                _strokePaint.PathEffect = null;

            canvas.DrawPath(_path, _strokePaint);

            // Optional markers
            if (Style.Marker != MarkerShape.None && renderCount <= 500)
            {
                _markerPaint.Color = Style.Color;
                float r = Style.MarkerSize * 0.5f;
                for (int i = 0; i < renderCount; i++)
                {
                    float px = converter.GetPixelX(renderX[i]);
                    float py = converter.GetPixelY(renderY[i]);
                    if (Style.Marker == MarkerShape.Circle)
                        canvas.DrawCircle(px, py, r, _markerPaint);
                    else if (Style.Marker == MarkerShape.Square)
                        canvas.DrawRect(px - r, py - r, r * 2, r * 2, _markerPaint);
                }
            }
        }
        finally
        {
            if (rentedX != null) ArrayPool<float>.Shared.Return(rentedX);
            if (rentedY != null) ArrayPool<float>.Shared.Return(rentedY);
        }
    }

    public void Dispose()
    {
        _path.Dispose();
        _fillPath.Dispose();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _markerPaint.Dispose();
    }
}
