namespace Glacier.Plot.Statistical;

using System;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Scatter plot overlay with an Ordinary Least Squares (OLS) regression line and confidence envelope.
/// </summary>
public sealed class RegressionPlot : IPlottable, IDisposable
{
    private const int ConfidenceSteps = 40;

    private readonly float[] _x;
    private readonly float[] _y;
    private readonly LinearRegressionResult _reg;
    private readonly float _minX;
    private readonly float _maxX;

    private readonly VectorPath _scatterPath = new();
    private readonly VectorPath _bandPath = new();
    private readonly VectorPath _linePath = new();

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan };
    public bool ShowConfidenceBand { get; set; } = true;
    public bool ShowScatter { get; set; } = true;

    public LinearRegressionResult Regression => _reg;

    public RegressionPlot(ReadOnlySpan<float> x, ReadOnlySpan<float> y, PlotStyle? style = null)
    {
        if (x.Length != y.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        _x = x.ToArray();
        _y = y.ToArray();
        _reg = RegressionKernels.FitLinear(x, y);

        if (style != null) Style = style;

        float minX = _x[0], maxX = _x[0];
        for (int i = 1; i < _x.Length; i++)
        {
            if (_x[i] < minX) minX = _x[i];
            if (_x[i] > maxX) maxX = _x[i];
        }

        if (maxX == minX) { minX -= 1f; maxX += 1f; }
        _minX = minX;
        _maxX = maxX;
    }

    public AxisLimits GetLimits()
    {
        return AxisLimits.FromData(_x, _y).WithPadding(0.05, 0.05);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_x.Length == 0) return;

        // 1. Draw Scatter Points
        if (ShowScatter)
        {
            _scatterPath.Clear();
            for (int i = 0; i < _x.Length; i++)
            {
                float px = converter.GetPixelX(_x[i]);
                float py = converter.GetPixelY(_y[i]);
                _scatterPath.AddCircle(px, py, 3.5f);
            }
            canvas.FillPath(_scatterPath, new Paint(Style.Color.WithAlpha(160), PaintStyle.Fill));
        }

        // 2. Draw Confidence Interval Envelope
        if (ShowConfidenceBand)
        {
            float stepSize = (_maxX - _minX) / (ConfidenceSteps - 1);
            Span<float> evalX = stackalloc float[ConfidenceSteps];
            Span<float> upperY = stackalloc float[ConfidenceSteps];
            Span<float> lowerY = stackalloc float[ConfidenceSteps];

            for (int i = 0; i < ConfidenceSteps; i++)
            {
                float curX = _minX + i * stepSize;
                evalX[i] = curX;
                var (lower, upper) = _reg.ConfidenceInterval(curX);
                lowerY[i] = lower;
                upperY[i] = upper;
            }

            _bandPath.Clear();
            _bandPath.MoveTo(converter.GetPixelX(evalX[0]), converter.GetPixelY(upperY[0]));

            for (int i = 1; i < ConfidenceSteps; i++)
            {
                _bandPath.LineTo(converter.GetPixelX(evalX[i]), converter.GetPixelY(upperY[i]));
            }

            for (int i = ConfidenceSteps - 1; i >= 0; i--)
            {
                _bandPath.LineTo(converter.GetPixelX(evalX[i]), converter.GetPixelY(lowerY[i]));
            }

            _bandPath.Close();

            canvas.FillPath(_bandPath, new Paint(Style.Color.WithAlpha(45), PaintStyle.Fill));
        }

        // 3. Draw Trend Line
        float px1 = converter.GetPixelX(_minX);
        float py1 = converter.GetPixelY(_reg.Predict(_minX));
        float px2 = converter.GetPixelX(_maxX);
        float py2 = converter.GetPixelY(_reg.Predict(_maxX));

        _linePath.Clear();
        _linePath.AddLine(px1, py1, px2, py2);
        canvas.DrawPath(_linePath, new Paint(Style.Color, PaintStyle.Stroke, Math.Max(2.0f, Style.StrokeWidth)));
    }

    public void Dispose()
    {
        _scatterPath.Clear();
        _bandPath.Clear();
        _linePath.Clear();
    }
}
