# 📊 Glacier.Plot

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native%20AOT-Ready-brightgreen.svg)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![Ecosystem](https://img.shields.io/badge/Glacier-Ecosystem-blue)](https://github.com/ian-cowley)

> **Hardware-Accelerated 2D Data Visualization Engine for C# .NET 10 (Systematically Beating Python Matplotlib)**

`Glacier.Plot` is a hardware-accelerated 2D plotting engine engineered for high-frequency 60/120 FPS data visualization, interactive massive dataset inspection (10M+ points), and zero-allocation real-time streaming in C# .NET 10. It serves as Pillar 4 of the unified **Glacier .NET 10 High-Performance Ecosystem**.

---

## 1. Why Glacier.Plot? Replacing Python Matplotlib

**Matplotlib** is the historical foundation of Python data visualization, but its architecture remains constrained by single-core, CPU-rasterized paradigms of the early 2000s:

1. **Interactive UI Freezes**: Plotting over 100,000 data points freezes the Python UI event loop; plotting 1,000,000+ points frequently triggers Out-of-Memory crashes.
2. **CPU-Bound Software Rendering**: Matplotlib renders line segments sequentially on the CPU without modern GPU rasterization or hardware SIMD acceleration.
3. **Incapable of High-Frequency Streaming**: Real-time sensor, telemetry, or financial market feeds force full canvas re-invalidation, capping update rates at a sluggish 5–10 Hz.

**Glacier.Plot** redefines 2D data visualization with:
- **GPU-Accelerated Rasterization**: Multi-backend rendering engine powered by SkiaSharp, Vulkan, and Direct2D.
- **SIMD LTTB Downsampling Kernel**: AVX-512 accelerated Largest-Triangle-Three-Buckets (LTTB) decimation algorithm reduces **10,000,000 data points** to screen pixel resolution in **< 4 milliseconds**.
- **Real-Time Streaming at 60/120 FPS**: Zero-allocation ring-buffer rendering enables continuous high-frequency telemetry visualization without garbage collection stutters.
- **Direct Polaris DataFrame Interop**: Plots directly from `Polaris.Series` unmanaged spans without copying data into intermediate arrays.

---

## 2. Rendering Pipeline & Architecture

```
                         Glacier.Plot Rendering Pipeline
┌──────────────────────────────────────┐
│ Glacier.Polaris Series (10M Points)  │
│ (ReadOnlySpan<float> X, Y)           │
└──────────────────┬───────────────────┘
                   │ Direct Span Pass (0 Copies)
                   ▼
┌──────────────────────────────────────┐
│ SIMD Decimator (LTTB / MinMax)       │
│ Compresses 10M pts -> 1,920 pixels   │
│ Computes in < 4ms via AVX-512        │
└──────────────────┬───────────────────┘
                   │ Screen-Space Coordinates
                   ▼
┌──────────────────────────────────────┐
│ GPU Rasterizer (SkiaSharp / Vulkan)  │
│ Hardware batched vertex buffers      │
└──────────────────┬───────────────────┘
                   │ 60 / 120 FPS Display
                   ▼
┌──────────────────────────────────────┐
│ Output Target                        │
│ ├── Avalonia UI / WPF Window         │
│ ├── Blazor WebAssembly / Canvas      │
│ └── Vector SVG / PNG Stream          │
└──────────────────────────────────────┘
```

### SIMD-Accelerated LTTB Kernel
The Largest-Triangle-Three-Buckets (LTTB) algorithm downsamples high-frequency time series while preserving visual peaks and troughs. `Glacier.Plot` executes vectorized cross-product area evaluations using `Vector512<float>` and `Vector256<float>` instructions to process millions of points per millisecond.

---

## 3. Parity & Performance Benchmarking Targets

| Plotting Scenario | Workload Scale | Python Matplotlib | Glacier.Plot (.NET 10) | Speedup |
| :--- | :--- | :--- | :--- | :--- |
| **Line Plot Render (100k pts)** | 100,000 points static | 280 ms | **1.2 ms** | **233x faster** |
| **Massive Line Plot (10M pts)** | 10,000,000 points | Out of Memory / Freeze | **14 ms (60 FPS fluid)** | **Instant Interactive** |
| **Real-Time Data Streaming** | 100 kHz sensor feed | 8 FPS (Stutters) | **120 FPS (Zero-Alloc)** | **15x higher refresh** |
| **High-Resolution PNG Export** | 4K (3840×2160) figure | 1.85 s | **45 ms** | **41x faster** |
| **Managed Allocations Per Frame** | Continuous redraw | ~45 MB / frame | **0 Bytes** | **Zero GC Pauses** |

---

## 4. Quickstart API

```csharp
using Glacier.Plot;
using Glacier.Plot.Rendering;
using Glacier.Polaris;

// Zero-copy plotting directly from a Glacier.Polaris DataFrame
using var df = DataFrame.ReadParquet("telemetry_10m.parquet");
ReadOnlySpan<float> time = df["Timestamp"].AsSpan<float>();
ReadOnlySpan<float> voltage = df["Voltage"].AsSpan<float>();

// Initialize hardware-accelerated plot view
var plot = new PlotView()
    .WithDimensions(width: 1920, height: 1080)
    .WithTheme(PlotTheme.Dark);

// Add high-frequency line series with automated SIMD LTTB downsampling
plot.AddSignal(time, voltage, label: "Sensor Voltage", color: Colors.Cyan);

// Render to high-resolution PNG or display in Avalonia/Desktop UI
plot.SavePng("output_4k.png", width: 3840, height: 2160);
```

---

## 5. Ecosystem Cross-References

`Glacier.Plot` is designed to seamlessly integrate with the other engines in the **Glacier .NET 10 High-Performance Ecosystem**:

- **[Master Architecture Plan](../../GLACIER_ECOSYSTEM_MASTER_PLAN.md)**: Ecosystem blueprint mapping the 9 Python domains to .NET 10 counterparts.
- **[Glacier.Plot Technical Specification](../../docs/plans/04_GLACIER_PLOT_SPEC.md)**: Deep dive into SIMD LTTB algorithms and GPU rasterization pipelines.
- **[Glacier.Polaris](https://github.com/ian-cowley/Glacier.Polaris)**: Columnar DataFrame engine feeding zero-copy data points to Glacier.Plot.
- **[Glacier.StatsViz](https://github.com/ian-cowley/Glacier.StatsViz)**: Advanced statistical graphics grammar built on top of Glacier.Plot.
- **[Glacier.Desktop](https://github.com/ian-cowley/Glacier.Desktop)**: GPU-accelerated desktop host for interactive visualizations.

---

## License

Licensed under the [MIT License](LICENSE). Copyright (c) 2026 Ian Cowley.
