namespace Glacier.Plot.Statistical;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Symmetric violin plot displaying continuous Kernel Density Estimation (KDE) and embedded inner quartiles.
/// </summary>
public sealed class ViolinPlot : IPlottable
{
    private readonly float[] _samples;
    private readonly float _centerX;
    private readonly float _width;
    private readonly SummaryStats _stats;
    private readonly float[] _gridY;
    private readonly float[] _density;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan, FillAlpha = 140 };
    public bool ShowBox { get; set; } = true;
    public bool ShowMedian { get; set; } = true;

    public ViolinPlot(float centerX, float[] samples, float width = 0.8f, float? bandwidth = null, PlotStyle? style = null)
    {
        if (samples.Length == 0)
            throw new ArgumentException("Samples cannot be empty.");

        _centerX = centerX;
        _width = width;
        _samples = samples;
        _stats = DescriptiveStats.Compute(samples.AsSpan());

        if (style != null) Style = style;

        float h = bandwidth ?? KdeKernels.SilvermanBandwidth(samples.AsSpan(), _stats.StdDev, _stats.IQR);
        if (h <= 0) h = 1.0f;

        const int gridPoints = 80;
        _gridY = new float[gridPoints];
        _density = new float[gridPoints];

        float pad = (_stats.Max - _stats.Min) * 0.1f;
        float yMin = _stats.Min - pad;
        float yMax = _stats.Max + pad;
        float step = (yMax - yMin) / (gridPoints - 1);

        for (int i = 0; i < gridPoints; i++) _gridY[i] = yMin + i * step;

        KdeKernels.VectorizedKde(samples.AsSpan(), _gridY, h, _density);

        // Normalize density so max spread matches width * 0.45
        float maxD = 0f;
        foreach (var d in _density) if (d > maxD) maxD = d;

        if (maxD > 0)
        {
            float scale = (_width * 0.45f) / maxD;
            for (int i = 0; i < gridPoints; i++) _density[i] *= scale;
        }
    }

    public ViolinPlot(float centerX, ReadOnlySpan<float> samples, float width = 0.8f, float? bandwidth = null, PlotStyle? style = null)
        : this(centerX, samples.ToArray(), width, bandwidth, style)
    {
    }

    public AxisLimits GetLimits()
    {
        float minX = _centerX - _width * 0.5f;
        float maxX = _centerX + _width * 0.5f;
        float minY = _gridY[0];
        float maxY = _gridY[^1];

        return new AxisLimits(minX, maxX, minY, maxY).WithPadding(0.05, 0.05);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_gridY.Length < 2) return;

        // 1. Draw Symmetric Violin Body
        var bodyPath = new VectorPath();
        int n = _gridY.Length;

        // Right side (ascending Y)
        float firstPxRight = converter.GetPixelX(_centerX + _density[0]);
        float firstPy = converter.GetPixelY(_gridY[0]);
        bodyPath.MoveTo(firstPxRight, firstPy);

        for (int i = 1; i < n; i++)
        {
            float px = converter.GetPixelX(_centerX + _density[i]);
            float py = converter.GetPixelY(_gridY[i]);
            bodyPath.LineTo(px, py);
        }

        // Left side (descending Y)
        for (int i = n - 1; i >= 0; i--)
        {
            float px = converter.GetPixelX(_centerX - _density[i]);
            float py = converter.GetPixelY(_gridY[i]);
            bodyPath.LineTo(px, py);
        }

        bodyPath.Close();

        canvas.FillPath(bodyPath, new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill));
        canvas.DrawPath(bodyPath, new Paint(Style.Color, PaintStyle.Stroke, Style.StrokeWidth));

        // 2. Embedded Miniature Box & Quartiles
        if (ShowBox && _stats.IQR > 0)
        {
            float pxCenter = converter.GetPixelX(_centerX);
            float pyQ1 = converter.GetPixelY(_stats.Q1);
            float pyQ3 = converter.GetPixelY(_stats.Q3);
            float pyLowWhisker = converter.GetPixelY(_stats.LowerWhisker);
            float pyHighWhisker = converter.GetPixelY(_stats.UpperWhisker);

            // Whisker line
            var whiskerPath = new VectorPath();
            whiskerPath.AddLine(pxCenter, pyLowWhisker, pxCenter, pyHighWhisker);
            canvas.DrawPath(whiskerPath, new Paint(Colors.White.WithAlpha(200), PaintStyle.Stroke, 2.0f));

            // Miniature IQR box
            float boxHalfWidth = 5.0f;
            float top = Math.Min(pyQ1, pyQ3);
            float bottom = Math.Max(pyQ1, pyQ3);

            var boxPath = new VectorPath();
            boxPath.AddRect(pxCenter - boxHalfWidth, top, boxHalfWidth * 2f, bottom - top);
            canvas.FillPath(boxPath, new Paint(Colors.DeepSlate, PaintStyle.Fill));
            canvas.DrawPath(boxPath, new Paint(Colors.White.WithAlpha(200), PaintStyle.Stroke, 2.0f));

            // White Median Dot
            if (ShowMedian)
            {
                float pyMedian = converter.GetPixelY(_stats.Median);
                var dotPath = new VectorPath();
                dotPath.AddCircle(pxCenter, pyMedian, 3.5f);
                canvas.FillPath(dotPath, new Paint(Colors.White, PaintStyle.Fill));
            }
        }
    }
}
