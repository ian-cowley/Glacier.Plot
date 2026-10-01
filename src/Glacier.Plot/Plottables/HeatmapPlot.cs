namespace Glacier.Plot.Plottables;

using System;
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
/// GPU-accelerated 2D matrix heatmap plottable with colormap interpolation.
/// </summary>
public sealed class HeatmapPlot : IPlottable
{
    private readonly float[] _data;
    private readonly int _rows;
    private readonly int _cols;
    private readonly float _minVal;
    private readonly float _maxVal;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new();
    public ColorMap Map { get; set; } = ColorMap.Viridis;

    public HeatmapPlot(ReadOnlySpan<float> flatData, int rows, int cols, ColorMap colormap = ColorMap.Viridis)
    {
        if (flatData.Length != rows * cols)
            throw new ArgumentException("Data length must equal rows * cols.");

        _data = flatData.ToArray();
        _rows = rows;
        _cols = cols;
        Map = colormap;

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

        float range = _maxVal - _minVal;
        float invRange = range > 0 ? 1.0f / range : 1.0f;

        var pixels = new Rgba32[_rows * _cols];

        for (int r = 0; r < _rows; r++)
        {
            // Invert row index so row 0 is at the bottom (Cartesian coordinates)
            int dataRow = _rows - 1 - r;
            int rowOffset = dataRow * _cols;

            for (int c = 0; c < _cols; c++)
            {
                float norm = Math.Clamp((_data[rowOffset + c] - _minVal) * invRange, 0f, 1f);
                pixels[r * _cols + c] = SampleColormap(norm, Map);
            }
        }

        float pxLeft = converter.GetPixelX(0);
        float pxRight = converter.GetPixelX(_cols);
        float pyTop = converter.GetPixelY(_rows);
        float pyBottom = converter.GetPixelY(0);

        var span2d = new ReadOnlySpan2D<Rgba32>(pixels, _cols, _rows, _cols);
        canvas.DrawImage(span2d, pxLeft, pyTop, Math.Abs(pxRight - pxLeft), Math.Abs(pyBottom - pyTop));
    }

    public static Rgba32 SampleColormap(float t, ColorMap map) => map switch
    {
        ColorMap.Viridis => Viridis(t),
        ColorMap.Plasma => Plasma(t),
        ColorMap.Inferno => Inferno(t),
        ColorMap.Coolwarm => Coolwarm(t),
        ColorMap.Grayscale => new Rgba32((byte)(t * 255), (byte)(t * 255), (byte)(t * 255)),
        _ => Viridis(t)
    };

    private static Rgba32 Viridis(float t)
    {
        // Polynomial approximation of Viridis colormap
        float r = Math.Clamp(0.28f + 1.25f * t - 1.6f * t * t + 0.97f * t * t * t, 0f, 1f);
        float g = Math.Clamp(0.01f + 1.4f * t - 0.45f * t * t, 0f, 1f);
        float b = Math.Clamp(0.33f + 1.2f * t - 2.8f * t * t + 1.4f * t * t * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static Rgba32 Plasma(float t)
    {
        float r = Math.Clamp(0.05f + 1.7f * t - 0.8f * t * t, 0f, 1f);
        float g = Math.Clamp(0.01f + 0.3f * t + 0.6f * t * t, 0f, 1f);
        float b = Math.Clamp(0.5f + 1.1f * t - 1.5f * t * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static Rgba32 Inferno(float t)
    {
        float r = Math.Clamp(t * 1.5f, 0f, 1f);
        float g = Math.Clamp(t * t * 1.2f, 0f, 1f);
        float b = Math.Clamp(0.2f * t + 0.6f * (1f - MathF.Abs(t - 0.4f)), 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static Rgba32 Coolwarm(float t)
    {
        float r = Math.Clamp(0.23f + 0.77f * t, 0f, 1f);
        float g = Math.Clamp(0.3f + 0.4f * (1f - MathF.Abs(t - 0.5f) * 2f), 0f, 1f);
        float b = Math.Clamp(0.85f - 0.7f * t, 0f, 1f);
        return new Rgba32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
