namespace Glacier.Plot.Figures;

using System;
using System.Collections.Generic;
using System.IO;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Text;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using Glacier.Plot.Rendering;

/// <summary>
/// High-performance 2D plotting canvas and figure orchestrator powered by Glacier.Graphics.
/// 100% pure managed C# .NET 10 raster and vector pipeline with zero native C++ dependencies.
/// </summary>
public sealed class Figure : IDisposable
{
    private readonly List<IPlottable> _plottables = new();
    private readonly VectorPath _dataRectPath = new();
    private readonly VectorPath _gridPath = new();
    private readonly VectorPath _tickPath = new();
    private readonly VectorPath _yAxisLabelPath = new();
    private readonly List<Tick> _xTicksBuffer = new(16);
    private readonly List<Tick> _yTicksBuffer = new(16);
    private int _paletteIndex = 0;

    private static readonly Font s_labelFont = new(11.0f);
    private static readonly Font s_titleFont = new(16.0f, bold: true);
    private static readonly Font s_axisTitleFont = new(12.0f, bold: true);

    public string? Title { get; set; }
    public Axis XAxis { get; } = new();
    public Axis YAxis { get; } = new();
    public PlotTheme Theme { get; set; } = PlotTheme.Dark;
    public bool ShowLegend { get; set; } = true;
    public LegendLocation LegendPosition { get; set; } = LegendLocation.TopRight;

    public IReadOnlyList<IPlottable> Plottables => _plottables;

    private Rgba32 GetNextPaletteColor()
    {
        var palette = Theme.Palette;
        if (palette == null || palette.Length == 0) return Colors.Cyan;
        var color = palette[_paletteIndex % palette.Length];
        _paletteIndex++;
        return color;
    }

    public SignalPlot PlotLine(ReadOnlyMemory<float> x, ReadOnlyMemory<float> y, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotSignal(ReadOnlyMemory<float> y, float xStart = 0f, float xStep = 1f, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(y, xStart, xStep, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotLine(float[] x, float[] y, string? label = null, Rgba32? color = null)
        => PlotLine((ReadOnlyMemory<float>)x, (ReadOnlyMemory<float>)y, label, color);

    public SignalPlot PlotSignal(float[] y, float xStart = 0f, float xStep = 1f, string? label = null, Rgba32? color = null)
        => PlotSignal((ReadOnlyMemory<float>)y, xStart, xStep, label, color);

    public SignalPlot PlotLine(ReadOnlySpan<float> x, ReadOnlySpan<float> y, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotSignal(ReadOnlySpan<float> y, float xStart = 0f, float xStep = 1f, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(y, xStart, xStep, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public ScatterPlot PlotScatter(ReadOnlyMemory<float> x, ReadOnlyMemory<float> y, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor(), Marker = MarkerShape.Circle };
        var plot = new ScatterPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public ScatterPlot PlotScatter(float[] x, float[] y, string? label = null, Rgba32? color = null)
        => PlotScatter((ReadOnlyMemory<float>)x, (ReadOnlyMemory<float>)y, label, color);

    public ScatterPlot PlotScatter(ReadOnlySpan<float> x, ReadOnlySpan<float> y, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor(), Marker = MarkerShape.Circle };
        var plot = new ScatterPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public BarPlot PlotBars(ReadOnlySpan<float> positions, ReadOnlySpan<float> values, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new BarPlot(positions, values, 0.8f, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public BarPlot PlotBars(ReadOnlySpan<string> categories, ReadOnlySpan<float> values, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new BarPlot(categories, values, 0.8f, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public HistogramPlot PlotHistogram(ReadOnlySpan<float> values, int bins = 30, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new HistogramPlot(values, bins, HistogramType.Count, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public HeatmapPlot PlotHeatmap(ReadOnlySpan<float> flatData, int rows, int cols, ColorMap colormap = ColorMap.Viridis)
    {
        var plot = new HeatmapPlot(flatData, rows, cols, colormap);
        _plottables.Add(plot);
        return plot;
    }

    public StreamingLinePlot PlotStream(int capacity, string? label = null, Rgba32? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new StreamingLinePlot(capacity, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public void AddPlottable(IPlottable plottable)
    {
        _plottables.Add(plottable);
    }

    public void Clear()
    {
        foreach (var p in _plottables)
        {
            if (p is IDisposable d) d.Dispose();
        }
        _plottables.Clear();
        _dataRectPath.Clear();
        _gridPath.Clear();
        _tickPath.Clear();
        _yAxisLabelPath.Clear();
        _paletteIndex = 0;
    }

    public AxisLimits ComputeEffectiveLimits()
    {
        AxisLimits total = AxisLimits.Empty;
        foreach (var p in _plottables)
        {
            total = total.Union(p.GetLimits());
        }

        if (!total.IsValid) total = AxisLimits.Default;

        double xMin = XAxis.AutoScale ? total.XMin : XAxis.Min;
        double xMax = XAxis.AutoScale ? total.XMax : XAxis.Max;
        double yMin = YAxis.AutoScale ? total.YMin : YAxis.Min;
        double yMax = YAxis.AutoScale ? total.YMax : YAxis.Max;

        return new AxisLimits(xMin, xMax, yMin, yMax);
    }

    public void Render(IGraphicsCanvas canvas, int width, int height)
    {
        var dims = new PlotDimensions(width, height);
        Render(canvas, dims);
    }

    public void Render(IGraphicsCanvas canvas, PlotDimensions dims)
    {
        var limits = ComputeEffectiveLimits();
        var conv = new CoordinateConverter(dims, limits);

        // 1. Fill Figure Background
        canvas.Clear(Theme.FigureBackground);

        // 2. Fill Data Area Background
        _dataRectPath.Clear();
        _dataRectPath.AddRect(dims.DataLeft, dims.DataTop, dims.DataWidth, dims.DataHeight);
        canvas.FillPath(_dataRectPath, new Paint(Theme.DataBackground, PaintStyle.Fill));

        // 3. Grid Lines & Ticks Calculation
        if (XAxis.ShowTicks || XAxis.ShowGrid)
            TickGenerator.Generate(limits.XMin, limits.XMax, 8, _xTicksBuffer);
        else
            _xTicksBuffer.Clear();

        if (YAxis.ShowTicks || YAxis.ShowGrid)
            TickGenerator.Generate(limits.YMin, limits.YMax, 6, _yTicksBuffer);
        else
            _yTicksBuffer.Clear();

        var gridPaint = new Paint(Theme.MajorGridColor, PaintStyle.Stroke, 1.0f);
        var axisLinePaint = new Paint(Theme.AxisColor, PaintStyle.Stroke, 1.5f);

        // Batch all X and Y gridlines into a single VectorPath
        _gridPath.Clear();
        if (XAxis.ShowGrid)
        {
            for (int i = 0; i < _xTicksBuffer.Count; i++)
            {
                float px = conv.GetPixelX(_xTicksBuffer[i].Value);
                if (px >= dims.DataLeft && px <= dims.DataRight)
                {
                    _gridPath.AddLine(px, dims.DataTop, px, dims.DataBottom);
                }
            }
        }

        if (YAxis.ShowGrid)
        {
            for (int i = 0; i < _yTicksBuffer.Count; i++)
            {
                float py = conv.GetPixelY(_yTicksBuffer[i].Value);
                if (py >= dims.DataTop && py <= dims.DataBottom)
                {
                    _gridPath.AddLine(dims.DataLeft, py, dims.DataRight, py);
                }
            }
        }

        if (_gridPath.VerbCount > 0)
        {
            canvas.DrawPath(_gridPath, gridPaint);
        }

        // 4. Render Plottables (clipped to data rect)
        canvas.Save();
        canvas.ClipPath(_dataRectPath);
        for (int i = 0; i < _plottables.Count; i++)
        {
            _plottables[i].Render(canvas, conv, Theme);
        }
        canvas.Restore();

        // 5. Draw Axis Bounding Box
        canvas.DrawPath(_dataRectPath, axisLinePaint);

        // 6. Draw Tick Marks (batched) & Labels
        var labelPaint = new Paint(Theme.LabelColor, PaintStyle.Fill);
        _tickPath.Clear();
        float maxYTickWidth = 0f;

        if (XAxis.ShowTicks)
        {
            for (int i = 0; i < _xTicksBuffer.Count; i++)
            {
                var t = _xTicksBuffer[i];
                float px = conv.GetPixelX(t.Value);
                if (px < dims.DataLeft - 1 || px > dims.DataRight + 1) continue;

                _tickPath.AddLine(px, dims.DataBottom, px, dims.DataBottom + 5f);

                float textWidth = s_labelFont.MeasureText(t.Label);
                canvas.DrawText(t.Label, px - textWidth * 0.5f, dims.DataBottom + 18f, s_labelFont, labelPaint);
            }
        }

        if (YAxis.ShowTicks)
        {
            for (int i = 0; i < _yTicksBuffer.Count; i++)
            {
                var t = _yTicksBuffer[i];
                float py = conv.GetPixelY(t.Value);
                if (py < dims.DataTop - 1 || py > dims.DataBottom + 1) continue;

                _tickPath.AddLine(dims.DataLeft - 5f, py, dims.DataLeft, py);

                float textWidth = s_labelFont.MeasureText(t.Label);
                if (textWidth > maxYTickWidth) maxYTickWidth = textWidth;
                canvas.DrawText(t.Label, dims.DataLeft - textWidth - 8f, py + 4f, s_labelFont, labelPaint);
            }
        }

        if (_tickPath.VerbCount > 0)
        {
            canvas.DrawPath(_tickPath, axisLinePaint);
        }

        // 7. Axis Titles
        if (!string.IsNullOrWhiteSpace(Title))
        {
            var titlePaint = new Paint(Theme.TitleColor, PaintStyle.Fill);
            float titleWidth = s_titleFont.MeasureText(Title);
            float titleX = dims.DataLeft + (dims.DataWidth - titleWidth) * 0.5f;
            float titleY = dims.MarginTop * 0.65f;
            canvas.DrawText(Title, titleX, titleY, s_titleFont, titlePaint);
        }

        if (!string.IsNullOrWhiteSpace(XAxis.Label))
        {
            var axisTitlePaint = new Paint(Theme.LabelColor, PaintStyle.Fill);
            float xLabelWidth = s_axisTitleFont.MeasureText(XAxis.Label);
            float xPos = dims.DataLeft + (dims.DataWidth - xLabelWidth) * 0.5f;
            float yPos = dims.Height - 12f;
            canvas.DrawText(XAxis.Label, xPos, yPos, s_axisTitleFont, axisTitlePaint);
        }

        if (!string.IsNullOrWhiteSpace(YAxis.Label))
        {
            RenderYAxisTitle(canvas, dims, maxYTickWidth);
        }

        // 8. Legend
        if (ShowLegend)
        {
            LegendRenderer.Render(canvas, _plottables, dims, Theme, LegendPosition);
        }

        canvas.Flush();
    }

    private void RenderYAxisTitle(IGraphicsCanvas canvas, PlotDimensions dims, float maxYTickWidth)
    {
        string labelText = YAxis.Label!;
        var axisTitlePaint = new Paint(Theme.LabelColor, PaintStyle.Fill);
        float yCenter = dims.DataTop + dims.DataHeight * 0.5f;
        float tickLeft = dims.DataLeft - maxYTickWidth - 8f;
        float xBaseline = Math.Max(14f, Math.Min(22f, tickLeft - 10f));

        if (canvas is SvgGraphicsCanvas svgCanvas)
        {
            svgCanvas.DrawTextRotated(labelText, xBaseline, yCenter, -90f, s_axisTitleFont, axisTitlePaint);
            return;
        }

        if (s_axisTitleFont.EffectiveTypeface is { } typeface)
        {
            _yAxisLabelPath.Clear();
            float labelWidth = s_axisTitleFont.MeasureText(labelText);
            float yStart = yCenter + labelWidth * 0.5f;
            float scale = s_axisTitleFont.Size / typeface.UnitsPerEm;
            float cursorOffset = 0f;

            for (int i = 0; i < labelText.Length; i++)
            {
                int codePoint = char.IsHighSurrogate(labelText[i]) && i + 1 < labelText.Length && char.IsLowSurrogate(labelText[i + 1])
                    ? char.ConvertToUtf32(labelText[i++], labelText[i])
                    : labelText[i];

                int glyphIdx = typeface.GetGlyphIndex(codePoint);
                VectorPath glyphPath = typeface.GetGlyphPath(glyphIdx);
                if (glyphPath.VerbCount > 0)
                {
                    var xform = new Matrix3x2(
                        0f, -scale,
                        -scale, 0f,
                        xBaseline, yStart - cursorOffset);
                    glyphPath.Transform(xform);
                    _yAxisLabelPath.AddPath(glyphPath);
                }

                ushort adv = typeface.GetAdvanceWidth(glyphIdx);
                cursorOffset += (adv / (float)typeface.UnitsPerEm) * s_axisTitleFont.Size;
            }

            if (_yAxisLabelPath.PointCount > 0)
            {
                canvas.FillPath(_yAxisLabelPath, axisTitlePaint);
                return;
            }
        }

        // Fallback when no TrueType outline is available: place cleanly above Y-ticks without overlap
        canvas.DrawText(labelText, 8f, Math.Max(14f, dims.DataTop - 10f), s_axisTitleFont, axisTitlePaint);
    }

    public LinearFramebuffer RenderToFramebuffer(int width = 1280, int height = 720)
    {
        var fb = new LinearFramebuffer(width, height);
        RenderToFramebuffer(fb);
        return fb;
    }

    public void RenderToFramebuffer(LinearFramebuffer fb)
    {
        using var canvas = new CpuGraphicsCanvas(fb);
        Render(canvas, fb.Width, fb.Height);
    }

    public byte[] RenderGlacierPng(int width = 1280, int height = 720)
    {
        using var fb = RenderToFramebuffer(width, height);
        return PngEncoder.Encode(fb);
    }

    public void RenderGlacierPng(Stream stream, int width, int height)
    {
        using var fb = RenderToFramebuffer(width, height);
        PngEncoder.Encode(fb, stream);
    }

    public byte[] RenderToBytes(int width = 1280, int height = 720)
    {
        return RenderGlacierPng(width, height);
    }

    public void RenderToStream(Stream stream, int width = 1280, int height = 720)
    {
        RenderGlacierPng(stream, width, height);
    }

    public void SavePng(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(filePath);
        RenderGlacierPng(stream, width, height);
    }

    public void SaveSvg(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var svgCanvas = new SvgGraphicsCanvas(width, height);
        Render(svgCanvas, width, height);
        File.WriteAllText(filePath, svgCanvas.ToString());
    }

    public void Dispose()
    {
        Clear();
    }
}
