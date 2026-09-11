namespace Glacier.Plot.Core;

/// <summary>
/// Configuration and limits for a single coordinate axis.
/// </summary>
public sealed class Axis
{
    public string? Label { get; set; }
    public double Min { get; set; } = 0.0;
    public double Max { get; set; } = 10.0;
    public bool AutoScale { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public bool ShowTicks { get; set; } = true;

    public void SetLimits(double min, double max)
    {
        if (double.IsNaN(min) || double.IsNaN(max) || min >= max) return;
        Min = min;
        Max = max;
        AutoScale = false;
    }

    public void Zoom(double factor, double centerFraction = 0.5)
    {
        if (factor <= 0) return;
        double span = Max - Min;
        double newSpan = span / factor;
        double center = Min + span * centerFraction;
        Min = center - newSpan * centerFraction;
        Max = Min + newSpan;
        AutoScale = false;
    }

    public void Pan(double deltaUnits)
    {
        Min += deltaUnits;
        Max += deltaUnits;
        AutoScale = false;
    }
}
