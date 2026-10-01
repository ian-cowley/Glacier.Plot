namespace Glacier.Plot.Figures;

using System.Collections.Generic;
using Glacier.Graphics;
using Glacier.Graphics.Text;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Plottables;

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
    private static readonly Font s_legendFont = new(12.0f);

    public static void Render(
        IGraphicsCanvas canvas,
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

        float itemHeight = 20.0f;
        float padding = 8.0f;
        float swatchWidth = 24.0f;
        float maxTextWidth = 0f;

        foreach (var item in items)
        {
            float w = s_legendFont.MeasureText(item.Label);
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

        // Draw background box
        var boxPath = new VectorPath();
        boxPath.AddRect(boxX, boxY, boxWidth, boxHeight);
        canvas.FillPath(boxPath, new Paint(theme.LegendBackground, PaintStyle.Fill));
        canvas.DrawPath(boxPath, new Paint(theme.LegendBorder, PaintStyle.Stroke, 1.0f));

        var textPaint = new Paint(theme.LegendText, PaintStyle.Fill);

        // Draw swatches and labels
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            float itemY = boxY + padding + i * itemHeight + itemHeight * 0.5f;

            float swatchX1 = boxX + padding;
            float swatchX2 = swatchX1 + swatchWidth;

            var swatchPath = new VectorPath();
            swatchPath.AddLine(swatchX1, itemY, swatchX2, itemY);
            canvas.DrawPath(swatchPath, new Paint(item.Style.Color, PaintStyle.Stroke, item.Style.StrokeWidth));

            if (item.Style.Marker != MarkerShape.None)
            {
                var markerPath = new VectorPath();
                markerPath.AddCircle((swatchX1 + swatchX2) * 0.5f, itemY, 3.5f);
                canvas.FillPath(markerPath, new Paint(item.Style.Color, PaintStyle.Fill));
            }

            float textX = swatchX2 + padding;
            float textY = itemY + 4.0f; // vertical text baseline adjustment
            canvas.DrawText(item.Label, textX, textY, s_legendFont, textPaint);
        }
    }
}
