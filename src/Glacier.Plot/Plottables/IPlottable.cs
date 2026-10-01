namespace Glacier.Plot.Plottables;

using Glacier.Graphics;
using Glacier.Plot.Core;

/// <summary>
/// Interface implemented by any visual plot element rendered onto the figure canvas.
/// </summary>
public interface IPlottable
{
    string? Label { get; set; }
    PlotStyle Style { get; set; }
    AxisLimits GetLimits();
    void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme);
    void Render(IGraphicsCanvas canvas, PlotDimensions dims) => Render(canvas, new CoordinateConverter(dims, GetLimits()), PlotTheme.Dark);
}
