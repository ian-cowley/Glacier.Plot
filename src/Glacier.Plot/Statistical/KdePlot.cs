namespace Glacier.Plot.Statistical;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Continuous 1D Kernel Density Estimation probability curve with optional area shading.
/// </summary>
public sealed class KdePlot : IPlottable
{
    private readonly float[] _grid;
    private readonly float[] _density;
    private readonly int _points;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan, IsFilled = true, FillAlpha = 80 };

    public KdePlot(ReadOnlySpan<float> samples, int gridResolution = 200, float? bandwidth = null, PlotStyle? style = null)
    {
        if (samples.Length == 0)
            throw new ArgumentException("Samples cannot be empty.");

        _points = gridResolution;
        if (style != null) Style = style;

        var stats = DescriptiveStats.Compute(samples);
        float h = bandwidth ?? KdeKernels.SilvermanBandwidth(samples, stats.StdDev, stats.IQR);
        if (h <= 0) h = 1.0f;

        float pad = (stats.Max - stats.Min) * 0.15f;
        if (pad <= 0) pad = 1.0f;

        float minX = stats.Min - pad;
        float maxX = stats.Max + pad;
        float step = (maxX - minX) / (_points - 1);

        _grid = new float[_points];
        _density = new float[_points];

        for (int i = 0; i < _points; i++) _grid[i] = minX + i * step;

        KdeKernels.VectorizedKde(samples, _grid, h, _density);
    }

    public AxisLimits GetLimits()
    {
        float maxD = 0f;
        foreach (var d in _density) if (d > maxD) maxD = d;
        if (maxD == 0) maxD = 1f;

        return new AxisLimits(_grid[0], _grid[^1], 0, maxD).WithPadding(0.02, 0.08);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_points < 2) return;

        var path = new VectorPath();
        path.MoveTo(converter.GetPixelX(_grid[0]), converter.GetPixelY(_density[0]));

        for (int i = 1; i < _points; i++)
        {
            path.LineTo(converter.GetPixelX(_grid[i]), converter.GetPixelY(_density[i]));
        }

        if (Style.IsFilled)
        {
            var fillPath = new VectorPath(path);
            float lastPx = converter.GetPixelX(_grid[^1]);
            float firstPx = converter.GetPixelX(_grid[0]);
            float baselinePy = converter.GetPixelY(0.0);
            fillPath.LineTo(lastPx, baselinePy);
            fillPath.LineTo(firstPx, baselinePy);
            fillPath.Close();

            canvas.FillPath(fillPath, new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill));
        }

        canvas.DrawPath(path, new Paint(
            Style.Color,
            PaintStyle.Stroke,
            Style.StrokeWidth,
            StrokeJoin.Round,
            StrokeCap.Round));
    }
}
