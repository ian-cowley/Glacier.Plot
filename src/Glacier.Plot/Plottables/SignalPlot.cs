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
public sealed class SignalPlot : IPlottable
{
    private readonly float[]? _xValues;
    private readonly float[] _yValues;
    private readonly int _count;
    private readonly bool _isUniform;
    private readonly float _xStart;
    private readonly float _xStep;
    private AxisLimits _cachedLimits = AxisLimits.Empty;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new();
    public DecimationStrategy Decimation { get; set; } = DecimationStrategy.Auto;

    /// <summary>
    /// Creates a signal plot with explicit X and Y coordinates.
    /// </summary>
    public SignalPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        _count = x.Length;
        _xValues = x.ToArray();
        _yValues = y.ToArray();
        _isUniform = false;
        if (style != null) Style = style;
    }

    /// <summary>
    /// Creates a signal plot with uniformly spaced Y coordinates (X = xStart + i * xStep).
    /// Eliminates memory allocation for X coordinates.
    /// </summary>
    public SignalPlot(ReadOnlySpan<float> y, float xStart = 0f, float xStep = 1f, PlotStyle? style = null)
    {
        _count = y.Length;
        _yValues = y.ToArray();
        _isUniform = true;
        _xStart = xStart;
        _xStep = xStep;
        if (style != null) Style = style;
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
            _cachedLimits = AxisLimits.FromDataUniform(_yValues.AsSpan(0, _count), _xStart, _xStep);
        }
        else
        {
            _cachedLimits = AxisLimits.FromData(_xValues.AsSpan(0, _count), _yValues.AsSpan(0, _count));
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
                    ? MinMaxKernels.DownsampleUniform(_yValues.AsSpan(0, _count), _xStart, _xStep, targetPixels, rentedX, rentedY)
                    : MinMaxKernels.Downsample(_xValues.AsSpan(0, _count), _yValues.AsSpan(0, _count), targetPixels, rentedX, rentedY);
            }
            else
            {
                renderCount = _isUniform
                    ? LttbKernels.DownsampleUniform(_yValues.AsSpan(0, _count), _xStart, _xStep, targetPixels, rentedX, rentedY)
                    : LttbKernels.Downsample(_xValues.AsSpan(0, _count), _yValues.AsSpan(0, _count), targetPixels, rentedX, rentedY);
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
                renderX = _xValues.AsSpan(0, _count);
            }
            renderY = _yValues.AsSpan(0, _count);
        }

        try
        {
            using var path = new SKPath();
            float firstPx = converter.GetPixelX(renderX[0]);
            float firstPy = converter.GetPixelY(renderY[0]);
            path.MoveTo(firstPx, firstPy);

            for (int i = 1; i < renderCount; i++)
            {
                float px = converter.GetPixelX(renderX[i]);
                float py = converter.GetPixelY(renderY[i]);
                path.LineTo(px, py);
            }

            // Fill under curve if enabled
            if (Style.IsFilled)
            {
                using var fillPath = new SKPath(path);
                float lastPx = converter.GetPixelX(renderX[renderCount - 1]);
                float baselinePy = converter.GetPixelY(Math.Max(0, converter.Limits.YMin));
                fillPath.LineTo(lastPx, baselinePy);
                fillPath.LineTo(firstPx, baselinePy);
                fillPath.Close();

                using var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = Style.Color.WithAlpha(Style.FillAlpha),
                    IsAntialias = true
                };
                canvas.DrawPath(fillPath, fillPaint);
            }

            // Stroke line
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = Style.Color,
                StrokeWidth = Style.StrokeWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            if (Style.Pattern == LinePattern.Dashed)
                strokePaint.PathEffect = SKPathEffect.CreateDash([10f, 6f], 0f);
            else if (Style.Pattern == LinePattern.Dotted)
                strokePaint.PathEffect = SKPathEffect.CreateDash([2f, 4f], 0f);
            else if (Style.Pattern == LinePattern.DashDot)
                strokePaint.PathEffect = SKPathEffect.CreateDash([10f, 4f, 2f, 4f], 0f);

            canvas.DrawPath(path, strokePaint);

            // Optional markers
            if (Style.Marker != MarkerShape.None && renderCount <= 500)
            {
                using var markerPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = Style.Color,
                    IsAntialias = true
                };

                float r = Style.MarkerSize * 0.5f;
                for (int i = 0; i < renderCount; i++)
                {
                    float px = converter.GetPixelX(renderX[i]);
                    float py = converter.GetPixelY(renderY[i]);
                    if (Style.Marker == MarkerShape.Circle)
                        canvas.DrawCircle(px, py, r, markerPaint);
                    else if (Style.Marker == MarkerShape.Square)
                        canvas.DrawRect(px - r, py - r, r * 2, r * 2, markerPaint);
                }
            }
        }
        finally
        {
            if (rentedX != null) ArrayPool<float>.Shared.Return(rentedX);
            if (rentedY != null) ArrayPool<float>.Shared.Return(rentedY);
        }
    }
}
