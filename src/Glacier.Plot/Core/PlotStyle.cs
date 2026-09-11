namespace Glacier.Plot.Core;

using SkiaSharp;

public enum LinePattern
{
    Solid,
    Dashed,
    Dotted,
    DashDot
}

public enum MarkerShape
{
    None,
    Circle,
    Square,
    Diamond,
    Cross,
    Plus
}

/// <summary>
/// Styling configuration for plottable series.
/// </summary>
public sealed class PlotStyle
{
    public SKColor Color { get; set; } = Colors.Cyan;
    public float StrokeWidth { get; set; } = 2.0f;
    public LinePattern Pattern { get; set; } = LinePattern.Solid;
    public MarkerShape Marker { get; set; } = MarkerShape.None;
    public float MarkerSize { get; set; } = 5.0f;
    public bool IsFilled { get; set; } = false;
    public byte FillAlpha { get; set; } = 64;

    public PlotStyle Clone() => new()
    {
        Color = Color,
        StrokeWidth = StrokeWidth,
        Pattern = Pattern,
        Marker = Marker,
        MarkerSize = MarkerSize,
        IsFilled = IsFilled,
        FillAlpha = FillAlpha
    };
}
