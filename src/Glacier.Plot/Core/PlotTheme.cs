namespace Glacier.Plot.Core;

using SkiaSharp;

/// <summary>
/// Styling theme defining background, foreground, grid, and palette colors.
/// </summary>
public sealed class PlotTheme
{
    public SKColor FigureBackground { get; set; } = Colors.DeepSlate;
    public SKColor DataBackground { get; set; } = new SKColor(0x18, 0x1B, 0x22);
    public SKColor AxisColor { get; set; } = new SKColor(0x60, 0x68, 0x7A);
    public SKColor MajorGridColor { get; set; } = new SKColor(0x28, 0x2E, 0x3D, 0x88);
    public SKColor MinorGridColor { get; set; } = new SKColor(0x20, 0x25, 0x33, 0x44);
    public SKColor TitleColor { get; set; } = Colors.White;
    public SKColor LabelColor { get; set; } = new SKColor(0xD0, 0xD4, 0xDC);
    public SKColor LegendBackground { get; set; } = new SKColor(0x1A, 0x1E, 0x27, 0xDD);
    public SKColor LegendBorder { get; set; } = new SKColor(0x40, 0x48, 0x5C);
    public SKColor LegendText { get; set; } = Colors.White;
    public SKColor[] Palette { get; set; } = Colors.DefaultPalette;

    public static PlotTheme Dark => new();

    public static PlotTheme Light => new()
    {
        FigureBackground = Colors.White,
        DataBackground = new SKColor(0xF9, 0xFA, 0xFB),
        AxisColor = new SKColor(0x9E, 0x9E, 0x9E),
        MajorGridColor = new SKColor(0xEE, 0xEE, 0xEE),
        MinorGridColor = new SKColor(0xF5, 0xF5, 0xF5),
        TitleColor = Colors.Black,
        LabelColor = new SKColor(0x42, 0x42, 0x42),
        LegendBackground = new SKColor(0xFF, 0xFF, 0xFF, 0xEE),
        LegendBorder = new SKColor(0xE0, 0xE0, 0xE0),
        LegendText = Colors.Black,
        Palette =
        [
            new SKColor(0x00, 0x7A, 0xFF), // Blue
            new SKColor(0x34, 0xC7, 0x59), // Green
            new SKColor(0xFF, 0x95, 0x00), // Orange
            new SKColor(0xFF, 0x2D, 0x55), // Pink
            new SKColor(0xAF, 0x52, 0xDE), // Purple
            new SKColor(0x58, 0x56, 0xD6), // Indigo
            new SKColor(0x00, 0xC7, 0xBE), // Teal
        ]
    };

    public static PlotTheme Cyber => new()
    {
        FigureBackground = new SKColor(0x0B, 0x0C, 0x10),
        DataBackground = new SKColor(0x1F, 0x28, 0x33),
        AxisColor = new SKColor(0x45, 0xA2, 0x9E),
        MajorGridColor = new SKColor(0x45, 0xA2, 0x9E, 0x40),
        MinorGridColor = new SKColor(0x45, 0xA2, 0x9E, 0x20),
        TitleColor = new SKColor(0x66, 0xFC, 0xF1),
        LabelColor = new SKColor(0xC5, 0xC6, 0xC7),
        LegendBackground = new SKColor(0x1F, 0x28, 0x33, 0xEE),
        LegendBorder = new SKColor(0x66, 0xFC, 0xF1),
        LegendText = new SKColor(0x66, 0xFC, 0xF1),
        Palette =
        [
            new SKColor(0x66, 0xFC, 0xF1), // Cyan Neon
            new SKColor(0xFF, 0x00, 0x7F), // Neon Pink
            new SKColor(0xFF, 0xE6, 0x00), // Neon Yellow
            new SKColor(0x00, 0xFF, 0x66), // Neon Green
            new SKColor(0x9D, 0x00, 0xFF)  // Neon Violet
        ]
    };
}
