namespace Glacier.Plot.Statistical;

using System;
using System.Collections.Generic;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Letter-value plot (boxen plot) for high-cardinality datasets, drawing successive
/// quantile boxes at diminishing widths to visualize tail distributions.
/// </summary>
public sealed class BoxenPlot : IPlottable
{
    private readonly float _centerX;
    private readonly float _baseWidth;
    private readonly float[] _sortedSamples;
    private readonly List<BoxenLevel> _levels = new();
    private readonly float _min;
    private readonly float _max;
    private readonly float _median;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.SteelBlue, FillAlpha = 180 };
    public bool ShowOutliers { get; set; } = true;

    private readonly struct BoxenLevel
    {
        public readonly float Lower;
        public readonly float Upper;
        public readonly float Width;

        public BoxenLevel(float lower, float upper, float width)
        {
            Lower = lower;
            Upper = upper;
            Width = width;
        }
    }

    public BoxenPlot(float centerX, ReadOnlySpan<float> samples, float baseWidth = 0.6f, int? maxLevels = null, PlotStyle? style = null)
    {
        _centerX = centerX;
        _baseWidth = baseWidth;
        if (style != null) Style = style;

        if (samples.IsEmpty)
        {
            _sortedSamples = Array.Empty<float>();
            _min = 0;
            _max = 0;
            _median = 0;
            return;
        }

        _sortedSamples = samples.ToArray();
        Array.Sort(_sortedSamples);

        _min = _sortedSamples[0];
        _max = _sortedSamples[^1];
        _median = ComputePercentile(_sortedSamples, 0.5);

        // Determine number of letter-value levels k = max(1, min(maxLevels, floor(log2(n)) - 3))
        int n = _sortedSamples.Length;
        int k = Math.Max(1, (int)Math.Floor(Math.Log2(Math.Max(8, n))) - 3);
        if (maxLevels.HasValue && maxLevels.Value > 0)
        {
            k = Math.Min(k, maxLevels.Value);
        }

        // Build levels symmetrically around median:
        // level 0: quartiles [0.25, 0.75]
        // level 1: octiles [0.125, 0.25] and [0.75, 0.875]
        // etc.
        float currentWidth = _baseWidth;
        double lowerP = 0.25;
        double upperP = 0.75;

        for (int level = 0; level < k; level++)
        {
            float lowVal = ComputePercentile(_sortedSamples, lowerP);
            float highVal = ComputePercentile(_sortedSamples, upperP);

            _levels.Add(new BoxenLevel(lowVal, highVal, currentWidth));

            currentWidth *= 0.75f; // Decreasing width for each successive quantile level
            lowerP /= 2.0;
            upperP = 1.0 - lowerP;
        }
    }

    private static float ComputePercentile(float[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0f;
        if (percentile <= 0.0) return sorted[0];
        if (percentile >= 1.0) return sorted[^1];

        double index = percentile * (sorted.Length - 1);
        int lowerIndex = (int)Math.Floor(index);
        int upperIndex = (int)Math.Ceiling(index);
        double fraction = index - lowerIndex;

        return (float)(sorted[lowerIndex] * (1.0 - fraction) + sorted[upperIndex] * fraction);
    }

    public AxisLimits GetLimits()
    {
        if (_sortedSamples.Length == 0) return new AxisLimits(0, 1, 0, 1);
        float minX = _centerX - _baseWidth * 0.5f;
        float maxX = _centerX + _baseWidth * 0.5f;
        return new AxisLimits(minX, maxX, _min, _max).WithPadding(0.05, 0.05);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_sortedSamples.Length == 0 || _levels.Count == 0) return;

        var strokePaint = new Paint(Style.Color, PaintStyle.Stroke, 1.2f);
        var fillPaint = new Paint(Style.Color.WithAlpha(Style.FillAlpha), PaintStyle.Fill);

        // Render each boxen level
        foreach (var lvl in _levels)
        {
            float pxLeft = converter.GetPixelX(_centerX - lvl.Width * 0.5f);
            float pxRight = converter.GetPixelX(_centerX + lvl.Width * 0.5f);
            float pyLow = converter.GetPixelY(lvl.Lower);
            float pyHigh = converter.GetPixelY(lvl.Upper);

            float top = Math.Min(pyLow, pyHigh);
            float bottom = Math.Max(pyLow, pyHigh);

            var boxPath = new VectorPath();
            boxPath.AddRect(pxLeft, top, pxRight - pxLeft, bottom - top);
            canvas.FillPath(boxPath, fillPaint);
            canvas.DrawPath(boxPath, strokePaint);
        }

        // Render median line across outermost box
        float pxMedianLeft = converter.GetPixelX(_centerX - _baseWidth * 0.5f);
        float pxMedianRight = converter.GetPixelX(_centerX + _baseWidth * 0.5f);
        float pyMedian = converter.GetPixelY(_median);

        var medianPath = new VectorPath();
        medianPath.AddLine(pxMedianLeft, pyMedian, pxMedianRight, pyMedian);
        canvas.DrawPath(medianPath, new Paint(Colors.White, PaintStyle.Stroke, 2.5f));
    }
}
