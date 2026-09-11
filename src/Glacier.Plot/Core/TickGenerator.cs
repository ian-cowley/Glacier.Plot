namespace Glacier.Plot.Core;

using System;
using System.Collections.Generic;

public readonly record struct Tick(double Value, string Label);

/// <summary>
/// Generates human-friendly, mathematically sound 1-2-5 tick intervals and formatted labels.
/// </summary>
public static class TickGenerator
{
    public static List<Tick> Generate(double min, double max, int approximateTicks = 8)
    {
        var result = new List<Tick>(approximateTicks + 2);
        if (double.IsNaN(min) || double.IsNaN(max) || min >= max || approximateTicks <= 1)
            return result;

        double span = max - min;
        double rawStep = span / approximateTicks;
        if (rawStep <= 0) return result;

        double power = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        double fraction = rawStep / power;

        double step;
        if (fraction < 1.5) step = 1.0 * power;
        else if (fraction < 3.0) step = 2.0 * power;
        else if (fraction < 7.0) step = 5.0 * power;
        else step = 10.0 * power;

        double firstTick = Math.Ceiling(min / step) * step;
        int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step)));

        for (double t = firstTick; t <= max + step * 0.0001; t += step)
        {
            // Avoid -0.0
            if (Math.Abs(t) < step * 0.0001) t = 0.0;

            string label = FormatTick(t, decimals, Math.Max(Math.Abs(min), Math.Abs(max)));
            result.Add(new Tick(t, label));
        }

        return result;
    }

    private static string FormatTick(double val, int decimals, double maxAbs)
    {
        if (maxAbs >= 1e6 || (maxAbs > 0 && maxAbs <= 1e-4))
            return val.ToString("0.##e+0");

        return val.ToString($"F{decimals}");
    }
}
