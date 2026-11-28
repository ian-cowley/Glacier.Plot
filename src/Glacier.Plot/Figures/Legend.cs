namespace Glacier.Plot.Figures;

using System;
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
/// Uses thread-local pooled collections and VectorPaths to avoid per-frame allocations.
/// </summary>
public static class LegendRenderer
{
    private static readonly Font s_legendFont = new(12.0f);

    [ThreadStatic] private static List<IPlottable>? t_items;
    [ThreadStatic] private static VectorPath? t_boxPath;
    [ThreadStatic] private static VectorPath? t_swatchPath;
    [ThreadStatic] private static VectorPath? t_markerPath;

    public static void Render(
        IGraphicsCanvas canvas,
        IReadOnlyList<IPlottable> plottables,
        PlotDimensions dimensions,
        PlotTheme theme,
        LegendLocation location = LegendLocation.TopRight)
    {
        var items = t_items ??= new List<IPlottable>();
        items.Clear();

        for (int i = 0; i < plottables.Count; i++)
        {
            var p = plottables[i];
            if (!string.IsNullOrWhiteSpace(p.Label)) items.Add(p);
        }

        if (items.Count == 0) return;

        float itemHeight = 20.0f;
        float padding = 8.0f;
        float swatchWidth = 24.0f;
        float maxTextWidth = 0f;

        for (int i = 0; i < items.Count; i++)
        {
            float w = s_legendFont.MeasureText(items[i].Label);
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

        var boxPath = t_boxPath ??= new VectorPath();
        boxPath.Clear();
        boxPath.AddRect(boxX, boxY, boxWidth, boxHeight);
        canvas.FillPath(boxPath, new Paint(theme.LegendBackground, PaintStyle.Fill));
        canvas.DrawPath(boxPath, new Paint(theme.LegendBorder, PaintStyle.Stroke, 1.0f));

        var textPaint = new Paint(theme.LegendText, PaintStyle.Fill);
        var swatchPath = t_swatchPath ??= new VectorPath();
        var markerPath = t_markerPath ??= new VectorPath();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            float itemY = boxY + padding + i * itemHeight + itemHeight * 0.5f;

            float swatchX1 = boxX + padding;
            float swatchX2 = swatchX1 + swatchWidth;

            swatchPath.Clear();
            swatchPath.AddLine(swatchX1, itemY, swatchX2, itemY);
            canvas.DrawPath(swatchPath, new Paint(item.Style.Color, PaintStyle.Stroke, item.Style.StrokeWidth));

            if (item.Style.Marker != MarkerShape.None)
            {
                markerPath.Clear();
                markerPath.AddCircle((swatchX1 + swatchX2) * 0.5f, itemY, 3.5f);
                canvas.FillPath(markerPath, new Paint(item.Style.Color, PaintStyle.Fill));
            }

            float textX = swatchX2 + padding;
            float textY = itemY + 4.0f;
            canvas.DrawText(item.Label, textX, textY, s_legendFont, textPaint);
        }

        items.Clear();
    }
}
