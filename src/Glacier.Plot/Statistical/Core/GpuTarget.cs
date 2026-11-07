namespace Glacier.Plot.Statistical.Core;

/// <summary>
/// Hardware execution target for Glacier.StatsViz statistical kernels.
/// </summary>
public enum GpuTarget
{
    Auto = 0,
    Cpu = 1,
    Nvidia = 2,
    Amd = 3
}
