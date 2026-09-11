namespace Glacier.Plot.Core;

using System.Runtime.CompilerServices;
using SkiaSharp;

/// <summary>
/// Fast zero-allocation coordinate transformer between data space and screen pixel space.
/// </summary>
public readonly struct CoordinateConverter
{
    public readonly PlotDimensions Dimensions;
    public readonly AxisLimits Limits;

    public readonly double PxPerUnitX;
    public readonly double PxPerUnitY;

    public CoordinateConverter(PlotDimensions dimensions, AxisLimits limits)
    {
        Dimensions = dimensions;
        Limits = limits.IsValid ? limits : AxisLimits.Default;

        double xSpan = Limits.XSpan != 0 ? Limits.XSpan : 1.0;
        double ySpan = Limits.YSpan != 0 ? Limits.YSpan : 1.0;

        PxPerUnitX = dimensions.DataWidth / xSpan;
        PxPerUnitY = dimensions.DataHeight / ySpan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetPixelX(double x) => (float)(Dimensions.DataLeft + (x - Limits.XMin) * PxPerUnitX);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float GetPixelY(double y) => (float)(Dimensions.DataBottom - (y - Limits.YMin) * PxPerUnitY);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SKPoint GetPixel(double x, double y) => new(GetPixelX(x), GetPixelY(y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDataX(float px) => Limits.XMin + (px - Dimensions.DataLeft) / PxPerUnitX;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetDataY(float py) => Limits.YMin + (Dimensions.DataBottom - py) / PxPerUnitY;
}
