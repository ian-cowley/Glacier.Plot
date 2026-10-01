namespace Glacier.Plot.Core;

using System;
using Glacier.Graphics;

/// <summary>
/// Dimensions and layout geometry for the rendered figure.
/// </summary>
public readonly record struct PlotDimensions(
    float Width,
    float Height,
    float MarginLeft = 70f,
    float MarginRight = 30f,
    float MarginTop = 50f,
    float MarginBottom = 60f,
    float DpiScale = 1.0f)
{
    public float DataWidth => Math.Max(10f, Width - MarginLeft - MarginRight);
    public float DataHeight => Math.Max(10f, Height - MarginTop - MarginBottom);

    public float DataLeft => MarginLeft;
    public float DataRight => Width - MarginRight;
    public float DataTop => MarginTop;
    public float DataBottom => Height - MarginBottom;

    public RectF DataRect => RectF.FromLTRB(DataLeft, DataTop, DataRight, DataBottom);
    public RectF FigureRect => new(0, 0, Width, Height);
}
