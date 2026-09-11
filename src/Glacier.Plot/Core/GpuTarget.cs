namespace Glacier.Plot.Core;

/// <summary>
/// Hardware execution target for Glacier.Plot data transforms and decimation.
/// </summary>
public enum GpuTarget
{
    Auto = 0,
    Cpu = 1,
    Nvidia = 2,
    Amd = 3
}
