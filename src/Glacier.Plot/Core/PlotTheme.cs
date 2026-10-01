namespace Glacier.Plot.Core;

using Glacier.Graphics;

/// <summary>
/// Styling theme defining background, foreground, grid, and palette colors.
/// </summary>
public sealed class PlotTheme
{
    public Rgba32 FigureBackground { get; set; } = Colors.DeepSlate;
    public Rgba32 DataBackground { get; set; } = new(0x18, 0x1B, 0x22);
    public Rgba32 AxisColor { get; set; } = new(0x60, 0x68, 0x7A);
    public Rgba32 MajorGridColor { get; set; } = new(0x28, 0x2E, 0x3D, 0x88);
    public Rgba32 MinorGridColor { get; set; } = new(0x20, 0x25, 0x33, 0x44);
    public Rgba32 TitleColor { get; set; } = Colors.White;
    public Rgba32 LabelColor { get; set; } = new(0xD0, 0xD4, 0xDC);
    public Rgba32 LegendBackground { get; set; } = new(0x1A, 0x1E, 0x27, 0xDD);
    public Rgba32 LegendBorder { get; set; } = new(0x40, 0x48, 0x5C);
    public Rgba32 LegendText { get; set; } = Colors.White;
    public Rgba32[] Palette { get; set; } = Colors.DefaultPalette;

    public static PlotTheme Dark => new();

    public static PlotTheme Light => new()
    {
        FigureBackground = Colors.White,
        DataBackground = new Rgba32(0xF9, 0xFA, 0xFB),
        AxisColor = new Rgba32(0x9E, 0x9E, 0x9E),
        MajorGridColor = new Rgba32(0xEE, 0xEE, 0xEE),
        MinorGridColor = new Rgba32(0xF5, 0xF5, 0xF5),
        TitleColor = Colors.Black,
        LabelColor = new Rgba32(0x42, 0x42, 0x42),
        LegendBackground = new Rgba32(0xFF, 0xFF, 0xFF, 0xEE),
        LegendBorder = new Rgba32(0xE0, 0xE0, 0xE0),
        LegendText = Colors.Black,
        Palette =
        [
            new Rgba32(0x00, 0x7A, 0xFF), // Blue
            new Rgba32(0x34, 0xC7, 0x59), // Green
            new Rgba32(0xFF, 0x95, 0x00), // Orange
            new Rgba32(0xFF, 0x2D, 0x55), // Pink
            new Rgba32(0xAF, 0x52, 0xDE), // Purple
            new Rgba32(0x58, 0x56, 0xD6), // Indigo
            new Rgba32(0x00, 0xC7, 0xBE), // Teal
        ]
    };

    public static PlotTheme Cyber => new()
    {
        FigureBackground = new Rgba32(0x0B, 0x0C, 0x10),
        DataBackground = new Rgba32(0x1F, 0x28, 0x33),
        AxisColor = new Rgba32(0x45, 0xA2, 0x9E),
        MajorGridColor = new Rgba32(0x45, 0xA2, 0x9E, 0x40),
        MinorGridColor = new Rgba32(0x45, 0xA2, 0x9E, 0x20),
        TitleColor = new Rgba32(0x66, 0xFC, 0xF1),
        LabelColor = new Rgba32(0xC5, 0xC6, 0xC7),
        LegendBackground = new Rgba32(0x1F, 0x28, 0x33, 0xEE),
        LegendBorder = new Rgba32(0x66, 0xFC, 0xF1),
        LegendText = new Rgba32(0x66, 0xFC, 0xF1),
        Palette =
        [
            new Rgba32(0x66, 0xFC, 0xF1), // Cyan Neon
            new Rgba32(0xFF, 0x00, 0x7F), // Neon Pink
            new Rgba32(0xFF, 0xE6, 0x00), // Neon Yellow
            new Rgba32(0x00, 0xFF, 0x66), // Neon Green
            new Rgba32(0x9D, 0x00, 0xFF)  // Neon Violet
        ]
    };
}
