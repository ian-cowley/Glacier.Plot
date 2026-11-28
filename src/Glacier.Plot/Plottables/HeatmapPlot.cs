namespace Glacier.Plot.Plottables;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Glacier.Graphics;
using Glacier.Plot.Core;

public enum ColorMap
{
    Viridis,
    Plasma,
    Inferno,
    Coolwarm,
    Grayscale
}

/// <summary>
/// 2D matrix heatmap plottable with cached colormap pixel buffer and SIMD normalization.
/// </summary>
public sealed class HeatmapPlot : IPlottable
{
    private readonly float[] _data;
    private readonly int _rows;
    private readonly int _cols;
    private readonly float _minVal;
    private readonly float _maxVal;
    private readonly Rgba32[] _pixels;
    private ColorMap _cachedMap;
    private bool _pixelsValid;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new();
    public ColorMap Map { get; set; } = ColorMap.Viridis;

    public HeatmapPlot(ReadOnlySpan<float> flatData, int rows, int cols, ColorMap colormap = ColorMap.Viridis)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        int expectedLength = checked(rows * cols);
        if (flatData.Length != expectedLength)
            throw new ArgumentException("Data length must equal rows * cols.");

        _data = flatData.ToArray();
        _rows = rows;
        _cols = cols;
        Map = colormap;
        _pixels = new Rgba32[expectedLength];

        float min = _data[0];
        float max = _data[0];
        foreach (var v in _data)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (max == min) max += 1f;
        _minVal = min;
        _maxVal = max;
    }

    public AxisLimits GetLimits() => new(0, _cols, 0, _rows);

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_rows == 0 || _cols == 0) return;

        if (!_pixelsValid || _cachedMap != Map)
        {
            PopulatePixels();
            _cachedMap = Map;
            _pixelsValid = true;
        }

        float pxLeft = converter.GetPixelX(0);
        float pxRight = converter.GetPixelX(_cols);
        float pyTop = converter.GetPixelY(_rows);
        float pyBottom = converter.GetPixelY(0);

        var span2d = new ReadOnlySpan2D<Rgba32>(_pixels, _cols, _rows, _cols);
        canvas.DrawImage(
            span2d,
            MathF.Min(pxLeft, pxRight),
            MathF.Min(pyTop, pyBottom),
            MathF.Abs(pxRight - pxLeft),
            MathF.Abs(pyBottom - pyTop));
    }

    private unsafe void PopulatePixels()
    {
        float range = _maxVal - _minVal;
        float invRange = range > 0 ? 1.0f / range : 1.0f;
        ColorMap map = Map;

        fixed (float* pData = _data)
        fixed (Rgba32* pOut = _pixels)
        {
            float* pNormScratch = stackalloc float[8];

            for (int r = 0; r < _rows; r++)
            {
                // Invert row index so row 0 is at the bottom (Cartesian coordinates)
                int dataRow = _rows - 1 - r;
                float* rowIn = pData + dataRow * _cols;
                Rgba32* rowOut = pOut + r * _cols;
                int c = 0;

                if (Vector256.IsHardwareAccelerated && _cols >= 8)
                {
                    var vMin = Vector256.Create(_minVal);
                    var vInv = Vector256.Create(invRange);
                    var vZero = Vector256<float>.Zero;
                    var vOne = Vector256.Create(1.0f);
                    int limit = _cols - 8;

                    for (; c <= limit; c += 8)
                    {
                        var v = Vector256.Load(rowIn + c);
                        var norm = Vector256.Min(Vector256.Max((v - vMin) * vInv, vZero), vOne);
                        norm.Store(pNormScratch);

                        for (int k = 0; k < 8; k++)
                        {
                            rowOut[c + k] = SampleColormap(pNormScratch[k], map);
                        }
                    }
                }

                for (; c < _cols; c++)
                {
                    float norm = Math.Clamp((rowIn[c] - _minVal) * invRange, 0f, 1f);
                    rowOut[c] = SampleColormap(norm, map);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba32 SampleColormap(float t, ColorMap map) => map switch
    {
        ColorMap.Viridis => Viridis(t),
        ColorMap.Plasma => Plasma(t),
        ColorMap.Inferno => Inferno(t),
        ColorMap.Coolwarm => Coolwarm(t),
        ColorMap.Grayscale => new Rgba32((byte)(t * 255), (byte)(t * 255), (byte)(t * 255)),
        _ => Viridis(t)
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Viridis(float t)
    {
        float r = Math.Clamp(0.28f + 1.25f * t - 1.6f * t * t + 0.97f * t * t * t, 0f, 1f);
        float g = Math.Clamp(0.01f + 1.4f * t - 0.45f * t * t, 0f, 1f);
        float b = Math.Clamp(0.33f + 1.2f * t - 2.8f * t * t + 1.4f * t * t * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Plasma(float t)
    {
        float r = Math.Clamp(0.05f + 1.7f * t - 0.8f * t * t, 0f, 1f);
        float g = Math.Clamp(0.01f + 0.3f * t + 0.6f * t * t, 0f, 1f);
        float b = Math.Clamp(0.5f + 1.1f * t - 1.5f * t * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Inferno(float t)
    {
        float r = Math.Clamp(t * 1.5f, 0f, 1f);
        float g = Math.Clamp(t * t * 1.2f, 0f, 1f);
        float b = Math.Clamp(0.2f * t + 0.6f * (1f - MathF.Abs(t - 0.4f)), 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba32 Coolwarm(float t)
    {
        float r = Math.Clamp(0.23f + 0.77f * t, 0f, 1f);
        float g = Math.Clamp(0.3f + 0.4f * (1f - MathF.Abs(t - 0.5f) * 2f), 0f, 1f);
        float b = Math.Clamp(0.85f - 0.7f * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
