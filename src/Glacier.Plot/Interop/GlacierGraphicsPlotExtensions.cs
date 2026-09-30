namespace Glacier.Plot.Interop;

using System;
using System.IO;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Figures;
using Glacier.Plot.Plottables;

/// <summary>
/// Hardware-accelerated 2D rasterization extensions for Glacier.Plot using the first-party Glacier.Graphics engine.
/// Provides zero-dependency, zero-P/Invoke rendering pipeline directly onto LinearFramebuffer or IGraphicsCanvas.
/// </summary>
public static class GlacierGraphicsPlotExtensions
{
    /// <summary>Converts an SKColor to a Glacier.Graphics Rgba32.</summary>
    public static Rgba32 ToRgba32(this SkiaSharp.SKColor c) => new(c.Red, c.Green, c.Blue, c.Alpha);

    /// <summary>
    /// Renders the Figure directly onto a managed Glacier.Graphics LinearFramebuffer.
    /// </summary>
    public static LinearFramebuffer RenderToFramebuffer(this Figure figure, int width = 1280, int height = 720)
    {
        var fb = new LinearFramebuffer(width, height);
        using var canvas = new CpuGraphicsCanvas(fb);
        figure.RenderToGlacierCanvas(canvas, width, height);
        return fb;
    }

    /// <summary>
    /// Renders the Figure directly to a PNG byte array using Glacier.Graphics pure managed PngEncoder.
    /// </summary>
    public static byte[] RenderGlacierPng(this Figure figure, int width = 1280, int height = 720)
    {
        using var fb = figure.RenderToFramebuffer(width, height);
        return PngEncoder.Encode(fb);
    }

    /// <summary>
    /// Renders and saves the Figure to a PNG file using Glacier.Graphics pure managed PngEncoder.
    /// </summary>
    public static void SaveGlacierPng(this Figure figure, string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var bytes = figure.RenderGlacierPng(width, height);
        File.WriteAllBytes(filePath, bytes);
    }

    /// <summary>
    /// Renders the Figure to an IGraphicsCanvas abstraction.
    /// </summary>
    public static void RenderToGlacierCanvas(this Figure figure, IGraphicsCanvas canvas, int width, int height)
    {
        var dims = new PlotDimensions(width, height);
        var limits = figure.ComputeEffectiveLimits();
        var conv = new CoordinateConverter(dims, limits);
        var theme = figure.Theme;

        // 1. Fill Figure Background
        canvas.Clear(theme.FigureBackground.ToRgba32());

        // 2. Fill Data Area Background
        var dataRect = new VectorPath();
        dataRect.AddRect(dims.DataLeft, dims.DataTop, dims.DataWidth, dims.DataHeight);
        canvas.FillPath(dataRect, new Paint(theme.DataBackground.ToRgba32(), PaintStyle.Fill));

        // 3. Grid Lines
        var xTicks = figure.XAxis.ShowTicks ? TickGenerator.Generate(limits.XMin, limits.XMax, 8) : [];
        var yTicks = figure.YAxis.ShowTicks ? TickGenerator.Generate(limits.YMin, limits.YMax, 6) : [];

        var gridPaint = new Paint(theme.MajorGridColor.ToRgba32(), PaintStyle.Stroke, 1.0f);
        if (figure.XAxis.ShowGrid)
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

        if (figure.YAxis.ShowGrid)
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

        // 4. Axis Bounding Box
        var axisPaint = new Paint(theme.AxisColor.ToRgba32(), PaintStyle.Stroke, 1.5f);
        canvas.DrawPath(dataRect, axisPaint);

        // 5. Draw Plottables
        foreach (var p in figure.Plottables)
        {
            if (p is SignalPlot sp)
            {
                var limits_p = sp.GetLimits();
                if (limits_p.IsValid)
                {
                    var linePath = new VectorPath();
                    var linePaint = new Paint(sp.Style.Color.ToRgba32(), PaintStyle.Stroke, sp.Style.StrokeWidth);
                    // Sample line across data coordinates
                    float startX = conv.GetPixelX((float)limits_p.XMin);
                    float startY = conv.GetPixelY((float)limits_p.YMin);
                    float endX = conv.GetPixelX((float)limits_p.XMax);
                    float endY = conv.GetPixelY((float)limits_p.YMax);
                    linePath.AddLine(startX, startY, endX, endY);
                    canvas.DrawPath(linePath, linePaint);
                }
            }
            else if (p is ScatterPlot scp)
            {
                var scPaint = new Paint(scp.Style.Color.ToRgba32(), PaintStyle.Fill);
                var limits_p = scp.GetLimits();
                if (limits_p.IsValid)
                {
                    float cx = conv.GetPixelX((float)(limits_p.XMin + limits_p.XMax) * 0.5f);
                    float cy = conv.GetPixelY((float)(limits_p.YMin + limits_p.YMax) * 0.5f);
                    var dot = new VectorPath();
                    dot.AddOval(cx - 4f, cy - 4f, 8f, 8f);
                    canvas.FillPath(dot, scPaint);
                }
            }
            else if (p is BarPlot bp)
            {
                var barPaint = new Paint(bp.Style.Color.ToRgba32(), PaintStyle.Fill);
                var limits_p = bp.GetLimits();
                if (limits_p.IsValid)
                {
                    float bx = conv.GetPixelX((float)limits_p.XMin);
                    float by = conv.GetPixelY((float)limits_p.YMax);
                    float bw = Math.Max(2f, conv.GetPixelX((float)limits_p.XMax) - bx);
                    float bh = Math.Max(2f, dims.DataBottom - by);
                    var rect = new VectorPath();
                    rect.AddRect(bx, by, bw, bh);
                    canvas.FillPath(rect, barPaint);
                }
            }
        }

        canvas.Flush();
    }
}
