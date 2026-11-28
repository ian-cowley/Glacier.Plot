# Glacier.Plot Benchmarks

All measurements below were collected on the reference workstation in `Release` x64 configuration during this audit.

## Test Workstation

- **CPU:** AMD Ryzen AI 9 HX 370 (12 cores / 24 logical threads, AVX-512)
- **Discrete GPU:** NVIDIA GeForce RTX 4060 Laptop GPU (8 GB VRAM, `sm_89`)
- **Memory & OS:** 31 GB RAM, Windows 11 (`10.0.26300.0`)
- **Runtime:** `.NET 10.0.15` (`.NET SDK 11.0.100`, Release x64)

## Commands

```powershell
# Default CPU SIMD + GPU CUDA decimation runner
dotnet run --project benchmarks/Glacier.Plot.Benchmarks/Glacier.Plot.Benchmarks.csproj -c Release

# Full BenchmarkDotNet suite (optional)
dotnet run --project benchmarks/Glacier.Plot.Benchmarks/Glacier.Plot.Benchmarks.csproj -c Release -- --bdn --filter "*"
```

## Measured Decimation Throughput

| Kernel & Backend | Workload Scale | Measured Latency | Throughput |
| :--- | :--- | ---: | ---: |
| `LttbKernels.DownsampleUniform` (CPU SIMD) | 100,000 pts -> 1,920 px | 0.102 ms | 982.7 M pts/s |
| `LttbKernels.DownsampleUniform` (CPU SIMD) | 1,000,000 pts -> 1,920 px | 0.383 ms | 2,611.5 M pts/s |
| `LttbKernels.DownsampleUniform` (CPU SIMD) | 10,000,000 pts -> 1,920 px | 2.558 ms | 3,909.8 M pts/s |
| `MinMaxKernels.DownsampleUniform` (CPU SIMD) | 10,000,000 pts -> 1,920 px | 1.756 ms | 5,693.5 M pts/s |
| `GpuPlotAccelerator.MinMaxDownsample` (RTX 4060 CUDA) | 1,000,000 pts -> 2,000 px | 0.796 ms | 1,255.5 M pts/s |
