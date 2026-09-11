namespace Glacier.Plot.Plottables;

using Glacier.Plot.Core;
using SkiaSharp;

/// <summary>
/// Interface implemented by any visual plot element rendered onto the figure canvas.
/// </summary>
public interface IPlottable
{
    string? Label { get; set; }
    PlotStyle Style { get; set; }
    AxisLimits GetLimits();
    void Render(SKCanvas canvas, CoordinateConverter converter, PlotTheme theme);
}
