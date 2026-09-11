namespace Glacier.Plot.Figures;

using System.Collections.Generic;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;
using SkiaSharp;

public enum LegendLocation
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft
}

/// <summary>
/// Automated legend box displaying labels and color swatches for plottables.
/// </summary>
public static class LegendRenderer
{
    public static void Render(
        SKCanvas canvas,
        IReadOnlyList<IPlottable> plottables,
        PlotDimensions dimensions,
        PlotTheme theme,
        LegendLocation location = LegendLocation.TopRight)
    {
        var items = new List<IPlottable>();
        foreach (var p in plottables)
        {
            if (!string.IsNullOrWhiteSpace(p.Label)) items.Add(p);
        }

        if (items.Count == 0) return;

        using var textPaint = new SKPaint
        {
            Color = theme.LegendText,
            TextSize = 12.0f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial")
        };

        float itemHeight = 20.0f;
        float padding = 8.0f;
        float swatchWidth = 24.0f;
        float maxTextWidth = 0f;

        foreach (var item in items)
        {
            float w = textPaint.MeasureText(item.Label);
            if (w > maxTextWidth) maxTextWidth = w;
        }

        float boxWidth = padding * 3 + swatchWidth + maxTextWidth;
        float boxHeight = padding * 2 + items.Count * itemHeight;

        float boxX = location switch
        {
            LegendLocation.TopLeft or LegendLocation.BottomLeft => dimensions.DataLeft + 12f,
            _ => dimensions.DataRight - boxWidth - 12f
        };

        float boxY = location switch
        {
            LegendLocation.BottomLeft or LegendLocation.BottomRight => dimensions.DataBottom - boxHeight - 12f,
            _ => dimensions.DataTop + 12f
        };

        var boxRect = new SKRect(boxX, boxY, boxX + boxWidth, boxY + boxHeight);

        // Draw background box
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = theme.LegendBackground, IsAntialias = true };
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = theme.LegendBorder, StrokeWidth = 1f, IsAntialias = true };

        canvas.DrawRoundRect(boxRect, 4f, 4f, bgPaint);
        canvas.DrawRoundRect(boxRect, 4f, 4f, borderPaint);

        // Draw swatches and labels
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            float itemY = boxY + padding + i * itemHeight + itemHeight * 0.5f;

            float swatchX1 = boxX + padding;
            float swatchX2 = swatchX1 + swatchWidth;

            using var swatchPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = item.Style.Color,
                StrokeWidth = item.Style.StrokeWidth,
                IsAntialias = true
            };

            canvas.DrawLine(swatchX1, itemY, swatchX2, itemY, swatchPaint);

            if (item.Style.Marker != MarkerShape.None)
            {
                using var mPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = item.Style.Color, IsAntialias = true };
                canvas.DrawCircle((swatchX1 + swatchX2) * 0.5f, itemY, 3.5f, mPaint);
            }

            float textX = swatchX2 + padding;
            float textY = itemY + 4.0f; // vertical text baseline adjustment
            canvas.DrawText(item.Label, textX, textY, textPaint);
        }
    }
}
