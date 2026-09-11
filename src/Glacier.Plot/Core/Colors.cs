namespace Glacier.Plot.Core;

using SkiaSharp;

/// <summary>
/// Predefined high-contrast, modern color constants for visualization.
/// </summary>
public static class Colors
{
    public static readonly SKColor Cyan = new(0x00, 0xE5, 0xFF);
    public static readonly SKColor SteelBlue = new(0x46, 0x82, 0xB4);
    public static readonly SKColor Emerald = new(0x00, 0xE6, 0x76);
    public static readonly SKColor Amber = new(0xFF, 0xC1, 0x07);
    public static readonly SKColor Crimson = new(0xDC, 0x14, 0x3C);
    public static readonly SKColor NeonPink = new(0xFF, 0x00, 0x7F);
    public static readonly SKColor Purple = new(0xD5, 0x00, 0xF9);
    public static readonly SKColor Orange = new(0xFF, 0x6D, 0x00);
    public static readonly SKColor Blue = new(0x29, 0x79, 0xFF);
    public static readonly SKColor Lime = new(0x76, 0xFF, 0x03);

    public static readonly SKColor White = new(0xFF, 0xFF, 0xFF);
    public static readonly SKColor Black = new(0x00, 0x00, 0x00);
    public static readonly SKColor Charcoal = new(0x1E, 0x1E, 0x1E);
    public static readonly SKColor DeepSlate = new(0x12, 0x14, 0x1A);
    public static readonly SKColor LightGray = new(0xEA, 0xEA, 0xEA);
    public static readonly SKColor DarkGray = new(0x2A, 0x2E, 0x39);
    public static readonly SKColor Transparent = new(0x00, 0x00, 0x00, 0x00);

    public static readonly SKColor[] DefaultPalette =
    [
        Cyan, Emerald, Amber, Crimson, Purple, Orange, Blue, Lime, SteelBlue, NeonPink
    ];
}
