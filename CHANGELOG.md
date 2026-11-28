# Changelog

All notable changes to `Glacier.Plot` are documented here.

## v1.2.2

- Updated ecosystem dependency alignment across `Glacier.Graphics`, `Glacier.Polaris`, `Glacier.Tensor`, and `Glacier.Gpu`.

## v1.2.1

- **Unified GPU Context (`CudaPlotContext` & `GpuTarget`):** Consolidated `GpuPlotAccelerator` and `GpuStatsAccelerator` onto a shared VRAM-capped CUDA context and stream pool with `KernelCompiler.NormalizePtxDirectives`, deterministic `ProcessExit` / `ReleaseGpuResources()` cleanup, and explicit `PlatformNotSupportedException` for unimplemented `GpuTarget.Amd` targets.
- **2D KDE & Vectorized Decimation:** Wired `GpuStatsAccelerator.EvaluateKde2D` and `KdeKernels.VectorizedKde2D`, vectorized `MinMaxKernels` in-register without redundant `IndexOf` rescans, and added `Vector512` / `Vector256` bucket averages and `SignalPlot` filled polygon envelopes.
- **Lock-Free SPSC Streaming & Zero-Copy Polaris:** Added 128-byte cacheline `PaddedCursor` sequence-stamped snapshots on `StreamingLinePlot`, pooled `VectorPath` instances across plottables and figures, zero-copy `Float32Series.Memory` interop, and rotated Y-axis titles.
- **Native AOT & Build Hygiene:** Enabled `<IsAotCompatible>true</IsAotCompatible>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
