namespace Glacier.Plot.Interop;

using System;
using System.IO;
using Glacier.Graphics;
using Glacier.Graphics.Raster;
using Glacier.Plot.Figures;

/// <summary>
/// Hardware-accelerated 2D rasterization extensions for Glacier.Plot using the first-party Glacier.Graphics engine.
/// Provides zero-dependency, zero-P/Invoke rendering pipeline directly onto LinearFramebuffer or IGraphicsCanvas.
/// </summary>
public static class GlacierGraphicsPlotExtensions
{
    /// <summary>
    /// Renders the Figure directly onto a managed Glacier.Graphics LinearFramebuffer.
    /// </summary>
    public static LinearFramebuffer RenderToFramebuffer(this Figure figure, int width = 1280, int height = 720)
    {
        return figure.RenderToFramebuffer(width, height);
    }

    /// <summary>
    /// Renders the Figure directly to a PNG byte array using Glacier.Graphics pure managed PngEncoder.
    /// </summary>
    public static byte[] RenderGlacierPng(this Figure figure, int width = 1280, int height = 720)
    {
        return figure.RenderGlacierPng(width, height);
    }

    /// <summary>
    /// Renders and saves the Figure to a PNG file using Glacier.Graphics pure managed PngEncoder.
    /// </summary>
    public static void SaveGlacierPng(this Figure figure, string filePath, int width = 1280, int height = 720)
    {
        figure.SavePng(filePath, width, height);
    }

    /// <summary>
    /// Renders the Figure to an IGraphicsCanvas abstraction.
    /// </summary>
    public static void RenderToGlacierCanvas(this Figure figure, IGraphicsCanvas canvas, int width, int height)
    {
        figure.Render(canvas, width, height);
    }
}
