namespace Glacier.Plot.Core;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Immutable 2D bounding limits defining the visible data range.
/// </summary>
public readonly record struct AxisLimits(double XMin, double XMax, double YMin, double YMax)
{
    public double XSpan => XMax - XMin;
    public double YSpan => YMax - YMin;

    public bool IsValid =>
        !double.IsNaN(XMin) && !double.IsNaN(XMax) &&
        !double.IsNaN(YMin) && !double.IsNaN(YMax) &&
        !double.IsInfinity(XMin) && !double.IsInfinity(XMax) &&
        !double.IsInfinity(YMin) && !double.IsInfinity(YMax) &&
        XMax >= XMin && YMax >= YMin;

    public static AxisLimits Default => new(0, 10, 0, 10);
    public static AxisLimits Empty => new(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);

    public AxisLimits Union(AxisLimits other)
    {
        if (!other.IsValid) return this;
        if (!IsValid) return other;

        return new AxisLimits(
            Math.Min(XMin, other.XMin),
            Math.Max(XMax, other.XMax),
            Math.Min(YMin, other.YMin),
            Math.Max(YMax, other.YMax)
        );
    }

    public AxisLimits WithPadding(double xFraction = 0.05, double yFraction = 0.05)
    {
        if (!IsValid) return this;

        double xPad = XSpan > 0 ? XSpan * xFraction : 1.0;
        double yPad = YSpan > 0 ? YSpan * yFraction : 1.0;

        return new AxisLimits(
            XMin - xPad,
            XMax + xPad,
            YMin - yPad,
            YMax + yPad
        );
    }

    public static unsafe AxisLimits FromData(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        if (x.Length == 0 || y.Length == 0) return Default;

        int len = Math.Min(x.Length, y.Length);
        float minX = x[0], maxX = x[0];
        float minY = y[0], maxY = y[0];

        fixed (float* pX = x)
        fixed (float* pY = y)
        {
            int i = 0;
            if (Avx2.IsSupported && len >= 8)
            {
                var vMinX = Vector256.Load(pX);
                var vMaxX = vMinX;
                var vMinY = Vector256.Load(pY);
                var vMaxY = vMinY;

                for (i = 8; i <= len - 8; i += 8)
                {
                    var vx = Vector256.Load(pX + i);
                    var vy = Vector256.Load(pY + i);
                    vMinX = Vector256.Min(vMinX, vx);
                    vMaxX = Vector256.Max(vMaxX, vx);
                    vMinY = Vector256.Min(vMinY, vy);
                    vMaxY = Vector256.Max(vMaxY, vy);
                }

                for (int k = 0; k < 8; k++)
                {
                    if (vMinX[k] < minX) minX = vMinX[k];
                    if (vMaxX[k] > maxX) maxX = vMaxX[k];
                    if (vMinY[k] < minY) minY = vMinY[k];
                    if (vMaxY[k] > maxY) maxY = vMaxY[k];
                }
            }

            for (; i < len; i++)
            {
                float vx = pX[i];
                float vy = pY[i];
                if (vx < minX) minX = vx;
                if (vx > maxX) maxX = vx;
                if (vy < minY) minY = vy;
                if (vy > maxY) maxY = vy;
            }
        }

        // Avoid degenerate zero range
        if (maxX == minX) { maxX += 1f; minX -= 1f; }
        if (maxY == minY) { maxY += 1f; minY -= 1f; }

        return new AxisLimits(minX, maxX, minY, maxY);
    }

    public static unsafe AxisLimits FromDataUniform(ReadOnlySpan<float> y, float xStart, float xStep)
    {
        if (y.Length == 0) return Default;

        int len = y.Length;
        float minX = xStart;
        float maxX = xStart + (len - 1) * xStep;
        float minY = y[0], maxY = y[0];

        fixed (float* pY = y)
        {
            int i = 0;
            if (Avx2.IsSupported && len >= 8)
            {
                var vMinY = Vector256.Load(pY);
                var vMaxY = vMinY;

                for (i = 8; i <= len - 8; i += 8)
                {
                    var vy = Vector256.Load(pY + i);
                    vMinY = Vector256.Min(vMinY, vy);
                    vMaxY = Vector256.Max(vMaxY, vy);
                }

                for (int k = 0; k < 8; k++)
                {
                    if (vMinY[k] < minY) minY = vMinY[k];
                    if (vMaxY[k] > maxY) maxY = vMaxY[k];
                }
            }

            for (; i < len; i++)
            {
                float vy = pY[i];
                if (vy < minY) minY = vy;
                if (vy > maxY) maxY = vy;
            }
        }

        if (maxX == minX) { maxX += 1f; minX -= 1f; }
        if (maxY == minY) { maxY += 1f; minY -= 1f; }

        return new AxisLimits(minX, maxX, minY, maxY);
    }
}
