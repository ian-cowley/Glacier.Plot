namespace Glacier.Plot.Decimation;

/// <summary>
/// Strategy used to downsample high-frequency time series or signals prior to rasterization.
/// </summary>
public enum DecimationStrategy
{
    /// <summary>
    /// Automatically selects optimal strategy (LTTB for moderate to large, MinMax for extreme density, None for small datasets).
    /// </summary>
    Auto,

    /// <summary>
    /// Largest-Triangle-Three-Buckets (LTTB) algorithm. Preserves visual peaks, troughs, and data distribution dynamics.
    /// </summary>
    Lttb,

    /// <summary>
    /// Min-Max envelope decimation. Computes minimum and maximum within each pixel column to preserve signal extremes and spikes.
    /// </summary>
    MinMax,

    /// <summary>
    /// No decimation. Passes all raw coordinates directly to the rasterizer.
    /// </summary>
    None
}
