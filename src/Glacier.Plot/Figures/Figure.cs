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
        var dataRect = new VectorPath();
        dataRect.AddRect(dims.DataLeft, dims.DataTop, dims.DataWidth, dims.DataHeight);
        canvas.FillPath(dataRect, new Paint(Theme.DataBackground, PaintStyle.Fill));

        // 3. Grid Lines & Ticks Calculation
        var xTicks = XAxis.ShowTicks ? TickGenerator.Generate(limits.XMin, limits.XMax, 8) : [];
        var yTicks = YAxis.ShowTicks ? TickGenerator.Generate(limits.YMin, limits.YMax, 6) : [];

        var gridPaint = new Paint(Theme.MajorGridColor, PaintStyle.Stroke, 1.0f);
        var axisLinePaint = new Paint(Theme.AxisColor, PaintStyle.Stroke, 1.5f);

        // Draw Grids
        if (XAxis.ShowGrid)
        {
            foreach (var t in xTicks)
            {
                float px = conv.GetPixelX(t.Value);
                if (px >= dims.DataLeft && px <= dims.DataRight)
                {
                    var line = new VectorPath();
                    line.AddLine(px, dims.DataTop, px, dims.DataBottom);
                    canvas.DrawPath(line, gridPaint);
                }
            }
        }

        if (YAxis.ShowGrid)
        {
            foreach (var t in yTicks)
            {
                float py = conv.GetPixelY(t.Value);
                if (py >= dims.DataTop && py <= dims.DataBottom)
                {
                    var line = new VectorPath();
                    line.AddLine(dims.DataLeft, py, dims.DataRight, py);
                    canvas.DrawPath(line, gridPaint);
                }
            }
        }

        // 4. Render Plottables (clipped to data rect)
        canvas.Save();
        canvas.ClipPath(dataRect);
        foreach (var p in _plottables)
        {
            p.Render(canvas, conv, Theme);
        }
        canvas.Restore();

        // 5. Draw Axis Bounding Box
        canvas.DrawPath(dataRect, axisLinePaint);

        // 6. Draw Tick Marks & Labels
        var labelPaint = new Paint(Theme.LabelColor, PaintStyle.Fill);
        if (XAxis.ShowTicks)
        {
            foreach (var t in xTicks)
            {
                float px = conv.GetPixelX(t.Value);
                if (px < dims.DataLeft - 1 || px > dims.DataRight + 1) continue;

                var tickLine = new VectorPath();
                tickLine.AddLine(px, dims.DataBottom, px, dims.DataBottom + 5f);
                canvas.DrawPath(tickLine, axisLinePaint);

                float textWidth = s_labelFont.MeasureText(t.Label);
                canvas.DrawText(t.Label, px - textWidth * 0.5f, dims.DataBottom + 18f, s_labelFont, labelPaint);
            }
        }

        if (YAxis.ShowTicks)
        {
            foreach (var t in yTicks)
            {
                float py = conv.GetPixelY(t.Value);
                if (py < dims.DataTop - 1 || py > dims.DataBottom + 1) continue;

                var tickLine = new VectorPath();
                tickLine.AddLine(dims.DataLeft - 5f, py, dims.DataLeft, py);
                canvas.DrawPath(tickLine, axisLinePaint);

                float textWidth = s_labelFont.MeasureText(t.Label);
                canvas.DrawText(t.Label, dims.DataLeft - textWidth - 8f, py + 4f, s_labelFont, labelPaint);
            }
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
            var axisTitlePaint = new Paint(Theme.LabelColor, PaintStyle.Fill);
            float yPos = dims.DataTop + dims.DataHeight * 0.5f;
            canvas.DrawText(YAxis.Label, 12f, yPos, s_axisTitleFont, axisTitlePaint);
        }

        // 8. Legend
        if (ShowLegend)
        {
            LegendRenderer.Render(canvas, _plottables, dims, Theme, LegendPosition);
        }

        canvas.Flush();
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
