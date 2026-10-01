namespace Glacier.Plot.Core;

using Glacier.Graphics;

/// <summary>
/// Predefined high-contrast, modern color constants for visualization.
/// </summary>
public static class Colors
{
    public static readonly Rgba32 Cyan = new(0x00, 0xE5, 0xFF);
    public static readonly Rgba32 SteelBlue = new(0x46, 0x82, 0xB4);
    public static readonly Rgba32 Emerald = new(0x00, 0xE6, 0x76);
    public static readonly Rgba32 Amber = new(0xFF, 0xC1, 0x07);
    public static readonly Rgba32 Crimson = new(0xDC, 0x14, 0x3C);
    public static readonly Rgba32 NeonPink = new(0xFF, 0x00, 0x7F);
    public static readonly Rgba32 Purple = new(0xD5, 0x00, 0xF9);
    public static readonly Rgba32 Orange = new(0xFF, 0x6D, 0x00);
    public static readonly Rgba32 Blue = new(0x29, 0x79, 0xFF);
    public static readonly Rgba32 Lime = new(0x76, 0xFF, 0x03);

    public static readonly Rgba32 White = new(0xFF, 0xFF, 0xFF);
    public static readonly Rgba32 Black = new(0x00, 0x00, 0x00);
    public static readonly Rgba32 Charcoal = new(0x1E, 0x1E, 0x1E);
    public static readonly Rgba32 DeepSlate = new(0x12, 0x14, 0x1A);
    public static readonly Rgba32 LightGray = new(0xEA, 0xEA, 0xEA);
    public static readonly Rgba32 DarkGray = new(0x2A, 0x2E, 0x39);
    public static readonly Rgba32 Transparent = new(0x00, 0x00, 0x00, 0x00);

    public static readonly Rgba32[] DefaultPalette =
    [
        Cyan, Emerald, Amber, Crimson, Purple, Orange, Blue, Lime, SteelBlue, NeonPink
    ];

    /// <summary>
    /// Creates a copy of the specified Rgba32 color with an altered alpha channel.
    /// </summary>
    public static Rgba32 WithAlpha(this Rgba32 c, byte alpha) => new(c.R, c.G, c.B, alpha);
}
