# Glacier.Plot User Manual

## 1. Figures, Axes & Rendering (`Figure`)

`Figure` (`Glacier.Plot.Figures`) manages axes, themes, legends, and plottables and renders directly into `Glacier.Graphics` canvases (`CpuGraphicsCanvas` / `LinearFramebuffer` / `SvgGraphicsCanvas`):

```csharp
using Glacier.Plot.Core;
using Glacier.Plot.Figures;

using var fig = new Figure
{
    Title = "Signal Diagnostics",
    Theme = PlotTheme.Dark,
    ShowLegend = true,
    LegendPosition = LegendLocation.TopRight
};
fig.XAxis.Label = "Time (ms)";
fig.YAxis.Label = "Amplitude";

fig.PlotLine(xArray, yArray, label: "Channel A", color: Colors.Cyan);
fig.SavePng("chart.png", width: 1920, height: 1080);
fig.SaveSvg("chart.svg", width: 1920, height: 1080);
```

Supported plottables on `Figure`:
- `PlotLine(x, y, label, color)` / `PlotSignal(y, xStart, xStep, ...)` -> `SignalPlot`
- `PlotScatter(x, y, label, color)` -> `ScatterPlot`
- `PlotBars(positionsOrCategories, values, ...)` -> `BarPlot`
- `PlotHistogram(values, bins, ...)` -> `HistogramPlot`
- `PlotHeatmap(flatData, rows, cols, colormap)` -> `HeatmapPlot`
- `PlotStream(capacity, ...)` -> `StreamingLinePlot` (ring-buffer telemetry)

For zero-allocation rendering onto existing framebuffers, call `fig.RenderToFramebuffer(fb)` or `fig.Render(canvas, width, height)`.

---

## 2. High-Speed Decimation & GPU Offload

### CPU Decimation (`LttbKernels` & `MinMaxKernels`)

```csharp
using Glacier.Plot.Decimation;

int written = LttbKernels.DownsampleUniform(samples, xStart: 0f, xStep: 1f, targetPoints: 1920, outX, outY);
int mmWritten = MinMaxKernels.DownsampleUniform(samples, xStart: 0f, xStep: 1f, bucketCount: 960, outX, outY);
```

### GPU Compute (`GpuPlotAccelerator` & `GpuStatsAccelerator`)

`GpuPlotAccelerator` (`Glacier.Plot.Compute`) runs NVIDIA CUDA PTX kernels via `CudaPlotContext` when `target` is `GpuTarget.Auto` (for inputs `>= 32,768` points) or `GpuTarget.Nvidia`, falling back to multi-core CPU SIMD on `GpuTarget.Auto` / `GpuTarget.Cpu`:

```csharp
using Glacier.Plot.Compute;
using Glacier.Plot.Core;

int count = GpuPlotAccelerator.MinMaxDownsample(xSpan, ySpan, targetPixelWidth: 2000, outX, outY, target: GpuTarget.Auto);
GpuPlotAccelerator.TransformCoordinates(...);
GpuPlotAccelerator.ComputeBucketAverages(...);

// Release pooled CUDA streams and device buffers cleanly when finished:
GpuPlotAccelerator.ReleaseGpuResources();
```

`GpuStatsAccelerator` (`Glacier.Plot.Statistical.Compute`) exposes `EvaluateKde` (1D) and `EvaluateKde2D` with matching `GpuTarget` routing and `ReleaseGpuResources()`.

---

## 3. Statistical Charts & Grammar (`Chart`)

Build multi-layer statistical plots directly from a `Glacier.Polaris.DataFrame` via `Glacier.Plot.Statistical.Grammar.Chart`:

```csharp
using Glacier.Plot.Statistical.Grammar;

var chart = Chart.FromDataFrame(df)
    .Encode(x: "Group", y: "LatencyMs", color: "Group")
    .WithTitle("Latency Distribution")
    .GeomViolin(showBox: true)
    .FacetGrid(col: "Region");

byte[] png = chart.UnderlyingFigure.RenderGlacierPng(1600, 900);
```

Standalone statistical plottables and kernels under `Glacier.Plot.Statistical` include `BoxPlot`, `BoxenPlot`, `ViolinPlot`, `KdePlot`, `RegressionPlot`, `DescriptiveStats`, `CorrelationKernels`, `KdeKernels`, and `RegressionKernels`.

---

## 4. Polaris & Tensor Interop

- **`PolarisPlotExtensions`:** Plot directly from `DataFrame` columns (`df.PlotLine("X", "Y")`, `df.PlotScatter(...)`, `df.PlotHistogram(...)`, or `fig.PlotLine(xSeries, ySeries)` via zero-copy `AsFloatMemory()`).
- **`TensorPlotExtensions`:** Plot 1D `Tensor<float>` waveforms (`fig.PlotLine(tensor)`) or 2D weight matrices (`fig.PlotHeatmap(matrix, ColorMap.Viridis)`).
- **`GlacierGraphicsPlotExtensions`:** Call `RenderToFramebuffer`, `RenderGlacierPng`, `SaveGlacierPng`, or `RenderToGlacierCanvas` on `Figure`.

---

## 5. Sample Runner

```powershell
dotnet run --project samples/Glacier.Plot.Demo/Glacier.Plot.Demo.csproj -c Release
```
