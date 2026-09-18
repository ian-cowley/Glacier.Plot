namespace Glacier.Plot.Figures;

using System;
using System.Collections.Generic;
using System.IO;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using SkiaSharp;

/// <summary>
/// High-performance 2D plotting canvas and figure orchestrator.
/// </summary>
public sealed class Figure : IDisposable
{
    private readonly List<IPlottable> _plottables = new();
    private int _paletteIndex = 0;

    private static readonly SKTypeface s_defaultTypeface = SKTypeface.FromFamilyName("Arial");
    private static readonly SKTypeface s_boldTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);

    private readonly SKPaint _bgPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _dataBgPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _gridPaint = new() { StrokeWidth = 1.0f, Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint _axisLinePaint = new() { StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint _labelPaint = new() { TextSize = 11.0f, IsAntialias = true, Typeface = s_defaultTypeface };
    private readonly SKPaint _titlePaint = new() { TextSize = 16.0f, IsAntialias = true, Typeface = s_boldTypeface };
    private readonly SKPaint _axisTitlePaint = new() { TextSize = 12.0f, IsAntialias = true, Typeface = s_boldTypeface };

    public string? Title { get; set; }
    public Axis XAxis { get; } = new();
    public Axis YAxis { get; } = new();
    public PlotTheme Theme { get; set; } = PlotTheme.Dark;
    public bool ShowLegend { get; set; } = true;
    public LegendLocation LegendPosition { get; set; } = LegendLocation.TopRight;

    public IReadOnlyList<IPlottable> Plottables => _plottables;

    private SKColor GetNextPaletteColor()
    {
        var palette = Theme.Palette;
        if (palette == null || palette.Length == 0) return Colors.Cyan;
        var color = palette[_paletteIndex % palette.Length];
        _paletteIndex++;
        return color;
    }

    public SignalPlot PlotLine(ReadOnlyMemory<float> x, ReadOnlyMemory<float> y, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotSignal(ReadOnlyMemory<float> y, float xStart = 0f, float xStep = 1f, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(y, xStart, xStep, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotLine(float[] x, float[] y, string? label = null, SKColor? color = null)
        => PlotLine((ReadOnlyMemory<float>)x, (ReadOnlyMemory<float>)y, label, color);

    public SignalPlot PlotSignal(float[] y, float xStart = 0f, float xStep = 1f, string? label = null, SKColor? color = null)
        => PlotSignal((ReadOnlyMemory<float>)y, xStart, xStep, label, color);

    public SignalPlot PlotLine(ReadOnlySpan<float> x, ReadOnlySpan<float> y, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public SignalPlot PlotSignal(ReadOnlySpan<float> y, float xStart = 0f, float xStep = 1f, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new SignalPlot(y, xStart, xStep, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public ScatterPlot PlotScatter(ReadOnlySpan<float> x, ReadOnlySpan<float> y, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor(), Marker = MarkerShape.Circle };
        var plot = new ScatterPlot(x, y, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public BarPlot PlotBars(ReadOnlySpan<float> positions, ReadOnlySpan<float> values, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new BarPlot(positions, values, 0.8f, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public BarPlot PlotBars(ReadOnlySpan<string> categories, ReadOnlySpan<float> values, string? label = null, SKColor? color = null)
    {
        var style = new PlotStyle { Color = color ?? GetNextPaletteColor() };
        var plot = new BarPlot(categories, values, 0.8f, style) { Label = label };
        _plottables.Add(plot);
        return plot;
    }

    public HistogramPlot PlotHistogram(ReadOnlySpan<float> values, int bins = 30, string? label = null, SKColor? color = null)
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

    public StreamingLinePlot PlotStream(int capacity, string? label = null, SKColor? color = null)
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

    public void Render(SKCanvas canvas, int width, int height)
    {
        var dims = new PlotDimensions(width, height);
        var limits = ComputeEffectiveLimits();
        var conv = new CoordinateConverter(dims, limits);

        // 1. Fill Figure Background
        _bgPaint.Color = Theme.FigureBackground;
        canvas.DrawRect(dims.FigureRect, _bgPaint);

        // 2. Fill Data Area Background
        _dataBgPaint.Color = Theme.DataBackground;
        canvas.DrawRect(dims.DataRect, _dataBgPaint);

        // 3. Grid Lines & Ticks Calculation
        var xTicks = XAxis.ShowTicks ? TickGenerator.Generate(limits.XMin, limits.XMax, 8) : [];
        var yTicks = YAxis.ShowTicks ? TickGenerator.Generate(limits.YMin, limits.YMax, 6) : [];

        _gridPaint.Color = Theme.MajorGridColor;
        _axisLinePaint.Color = Theme.AxisColor;
        _labelPaint.Color = Theme.LabelColor;

        // Draw Grids
        if (XAxis.ShowGrid)
        {
            foreach (var t in xTicks)
            {
                float px = conv.GetPixelX(t.Value);
                if (px >= dims.DataLeft && px <= dims.DataRight)
                    canvas.DrawLine(px, dims.DataTop, px, dims.DataBottom, _gridPaint);
            }
        }

        if (YAxis.ShowGrid)
        {
            foreach (var t in yTicks)
            {
                float py = conv.GetPixelY(t.Value);
                if (py >= dims.DataTop && py <= dims.DataBottom)
                    canvas.DrawLine(dims.DataLeft, py, dims.DataRight, py, _gridPaint);
            }
        }

        // 4. Render Plottables (clipped to data rect)
        canvas.Save();
        canvas.ClipRect(dims.DataRect);
        foreach (var p in _plottables)
        {
            p.Render(canvas, conv, Theme);
        }
        canvas.Restore();

        // 5. Draw Axis Bounding Box
        canvas.DrawRect(dims.DataRect, _axisLinePaint);

        // 6. Draw Tick Marks & Labels
        if (XAxis.ShowTicks)
        {
            foreach (var t in xTicks)
            {
                float px = conv.GetPixelX(t.Value);
                if (px < dims.DataLeft - 1 || px > dims.DataRight + 1) continue;

                canvas.DrawLine(px, dims.DataBottom, px, dims.DataBottom + 5f, _axisLinePaint);
                float textWidth = _labelPaint.MeasureText(t.Label);
                canvas.DrawText(t.Label, px - textWidth * 0.5f, dims.DataBottom + 18f, _labelPaint);
            }
        }

        if (YAxis.ShowTicks)
        {
            foreach (var t in yTicks)
            {
                float py = conv.GetPixelY(t.Value);
                if (py < dims.DataTop - 1 || py > dims.DataBottom + 1) continue;

                canvas.DrawLine(dims.DataLeft - 5f, py, dims.DataLeft, py, _axisLinePaint);
                float textWidth = _labelPaint.MeasureText(t.Label);
                canvas.DrawText(t.Label, dims.DataLeft - textWidth - 8f, py + 4f, _labelPaint);
            }
        }

        // 7. Axis Titles
        _titlePaint.Color = Theme.TitleColor;
        if (!string.IsNullOrWhiteSpace(Title))
        {
            float titleWidth = _titlePaint.MeasureText(Title);
            float titleX = dims.DataLeft + (dims.DataWidth - titleWidth) * 0.5f;
            float titleY = dims.MarginTop * 0.65f;
            canvas.DrawText(Title, titleX, titleY, _titlePaint);
        }

        _axisTitlePaint.Color = Theme.LabelColor;
        if (!string.IsNullOrWhiteSpace(XAxis.Label))
        {
            float xLabelWidth = _axisTitlePaint.MeasureText(XAxis.Label);
            float xPos = dims.DataLeft + (dims.DataWidth - xLabelWidth) * 0.5f;
            float yPos = height - 12f;
            canvas.DrawText(XAxis.Label, xPos, yPos, _axisTitlePaint);
        }

        if (!string.IsNullOrWhiteSpace(YAxis.Label))
        {
            canvas.Save();
            canvas.Translate(18f, dims.DataTop + dims.DataHeight * 0.5f);
            canvas.RotateDegrees(-90);
            float yLabelWidth = _axisTitlePaint.MeasureText(YAxis.Label);
            canvas.DrawText(YAxis.Label, -yLabelWidth * 0.5f, 0, _axisTitlePaint);
            canvas.Restore();
        }

        // 8. Legend
        if (ShowLegend)
        {
            LegendRenderer.Render(canvas, _plottables, dims, Theme, LegendPosition);
        }
    }

    public SKImage RenderToImage(int width = 1280, int height = 720)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Render(surface.Canvas, width, height);
        return surface.Snapshot();
    }

    public byte[] RenderToBytes(int width = 1280, int height = 720, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using var image = RenderToImage(width, height);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    public void RenderToStream(Stream stream, int width = 1280, int height = 720, SKEncodedImageFormat format = SKEncodedImageFormat.Png, int quality = 100)
    {
        using var image = RenderToImage(width, height);
        using var data = image.Encode(format, quality);
        data.SaveTo(stream);
    }

    public void SavePng(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(filePath);
        RenderToStream(stream, width, height, SKEncodedImageFormat.Png, 100);
    }

    public void SaveJpeg(string filePath, int width = 1280, int height = 720, int quality = 90)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(filePath);
        RenderToStream(stream, width, height, SKEncodedImageFormat.Jpeg, quality);
    }

    public void SaveWebp(string filePath, int width = 1280, int height = 720, int quality = 90)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(filePath);
        RenderToStream(stream, width, height, SKEncodedImageFormat.Webp, quality);
    }

    public void SaveSvg(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(filePath);
        using var wstream = new SKManagedWStream(stream);
        using var canvas = SKSvgCanvas.Create(new SKRect(0, 0, width, height), wstream);
        Render(canvas, width, height);
    }

    public void Dispose()
    {
        Clear();
        _bgPaint.Dispose();
        _dataBgPaint.Dispose();
        _gridPaint.Dispose();
        _axisLinePaint.Dispose();
        _labelPaint.Dispose();
        _titlePaint.Dispose();
        _axisTitlePaint.Dispose();
    }
}
