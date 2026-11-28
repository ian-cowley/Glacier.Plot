using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Glacier.Gpu.Drivers;
using Glacier.Plot.Compute;
using Glacier.Plot.Core;
using Glacier.Plot.Statistical.Kernels;

namespace Glacier.Plot.Statistical.Compute;

/// <summary>
/// Bare-metal GPU hardware accelerator for Glacier.Plot statistical kernels.
/// Accelerates 1D and 2D Kernel Density Estimation (KDE) via shared <see cref="CudaPlotContext"/>
/// on NVIDIA CUDA GPUs, with multi-threaded AVX-512 / AVX2 / AdvSimd CPU fallback.
/// </summary>
public static unsafe class GpuStatsAccelerator
{
    /// <summary>Gets whether NVIDIA CUDA PTX hardware acceleration is initialized and available.</summary>
    public static bool IsNvidiaAvailable => CudaPlotContext.IsNvidiaAvailable;

    /// <summary>
    /// Gets whether AMD HIP GPU kernel offload is available.
    /// Always <see langword="false"/>; embedded compute kernels target NVVM PTX, and <see cref="GpuTarget.Amd"/> throws <see cref="PlatformNotSupportedException"/>.
    /// </summary>
    public static bool IsAmdAvailable => false;

    /// <summary>Gets whether bare-metal GPU kernel acceleration is available.</summary>
    public static bool IsGpuAvailable => IsNvidiaAvailable;

    /// <summary>
    /// Releases all pooled CUDA streams, device buffers, PTX modules, and the shared CUDA context.
    /// </summary>
    public static void ReleaseGpuResources() => CudaPlotContext.ReleaseGpuResources();

    #region Kernel Density Estimation (1D)

    public static void EvaluateKde(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        float bandwidth,
        Span<float> outDensity,
        GpuTarget target = GpuTarget.Auto)
    {
        CudaPlotContext.ValidateTarget(target);
        if (bandwidth <= 0 || !float.IsFinite(bandwidth))
            throw new ArgumentOutOfRangeException(nameof(bandwidth), "Bandwidth must be finite and positive.");

        int numSamples = samples.Length;
        int numGrid = grid.Length;
        if (numSamples == 0 || numGrid == 0) return;
        if (outDensity.Length < numGrid)
            throw new ArgumentException("Output buffer too small.");

        float invH2 = 1.0f / (2.0f * bandwidth * bandwidth);
        float normFactor = 1.0f / (numSamples * bandwidth * MathF.Sqrt(2.0f * MathF.PI));

        if (target == GpuTarget.Nvidia)
        {
            if (TryExecuteGpuKde1D(samples, grid, outDensity, numSamples, numGrid, invH2, normFactor))
                return;

            if (!IsNvidiaAvailable)
            {
                throw new PlatformNotSupportedException(
                    "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
            }

            throw new InvalidOperationException(
                "NVIDIA CUDA execution failed for GpuStatsAccelerator.EvaluateKde.");
        }

        long workItems = (long)numSamples * numGrid;
        if (target == GpuTarget.Auto && IsNvidiaAvailable && workItems >= 32768 &&
            TryExecuteGpuKde1D(samples, grid, outDensity, numSamples, numGrid, invH2, normFactor))
        {
            return;
        }

        KdeKernels.VectorizedKde(samples, grid, bandwidth, outDensity);
    }

    #endregion

    #region Kernel Density Estimation (2D)

    /// <summary>
    /// Evaluates a 2D separable Gaussian Kernel Density Estimate over a Cartesian grid (<paramref name="xGrid"/> x <paramref name="yGrid"/>),
    /// storing results row-major (<c>outDensity[y * xGrid.Length + x]</c>).
    /// Dispatches to <c>statsviz_kde2d_evaluate_fp32</c> on NVIDIA CUDA GPUs or SIMD CPU fallback.
    /// </summary>
    public static void EvaluateKde2D(
        ReadOnlySpan<float> xSamples,
        ReadOnlySpan<float> ySamples,
        ReadOnlySpan<float> xGrid,
        ReadOnlySpan<float> yGrid,
        float bandwidthX,
        float bandwidthY,
        Span<float> outDensity,
        GpuTarget target = GpuTarget.Auto)
    {
        CudaPlotContext.ValidateTarget(target);
        if (xSamples.Length != ySamples.Length)
            throw new ArgumentException("X and Y sample spans must have identical length.");
        if (bandwidthX <= 0 || !float.IsFinite(bandwidthX))
            throw new ArgumentOutOfRangeException(nameof(bandwidthX), "BandwidthX must be finite and positive.");
        if (bandwidthY <= 0 || !float.IsFinite(bandwidthY))
            throw new ArgumentOutOfRangeException(nameof(bandwidthY), "BandwidthY must be finite and positive.");

        int numSamples = xSamples.Length;
        int nx = xGrid.Length;
        int ny = yGrid.Length;
        if (numSamples == 0 || nx == 0 || ny == 0) return;

        int gridTotal = checked(nx * ny);
        if (outDensity.Length < gridTotal)
            throw new ArgumentException("Output buffer too small for nx * ny 2D KDE grid.");

        float invHx2 = 1.0f / (bandwidthX * bandwidthX);
        float invHy2 = 1.0f / (bandwidthY * bandwidthY);
        float normFactor = 1.0f / (numSamples * 2.0f * MathF.PI * bandwidthX * bandwidthY);

        if (target == GpuTarget.Nvidia)
        {
            if (TryExecuteGpuKde2D(xSamples, ySamples, xGrid, yGrid, outDensity, numSamples, nx, ny, gridTotal, invHx2, invHy2, normFactor))
                return;

            if (!IsNvidiaAvailable)
            {
                throw new PlatformNotSupportedException(
                    "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
            }

            throw new InvalidOperationException(
                "NVIDIA CUDA execution failed for GpuStatsAccelerator.EvaluateKde2D.");
        }

        long workItems = (long)numSamples * gridTotal;
        if (target == GpuTarget.Auto && IsNvidiaAvailable && workItems >= 32768 &&
            TryExecuteGpuKde2D(xSamples, ySamples, xGrid, yGrid, outDensity, numSamples, nx, ny, gridTotal, invHx2, invHy2, normFactor))
        {
            return;
        }

        KdeKernels.VectorizedKde2D(xSamples, ySamples, xGrid, yGrid, bandwidthX, bandwidthY, outDensity);
    }

    #endregion

    #region CUDA Dispatch

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryExecuteGpuKde1D(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        Span<float> outDensity,
        int numSamples,
        int numGrid,
        float invH2,
        float normFactor)
    {
        if (!CudaPlotContext.TryEnterNvidiaExecutionReadLock())
            return false;

        try
        {
            if (CudaPlotContext.Kde1DFn == IntPtr.Zero)
                return false;

            nuint bytesSamples = CudaPlotContext.ComputeFloatBufferBytes(numSamples);
            nuint bytesGrid = CudaPlotContext.ComputeFloatBufferBytes(numGrid);
            nuint bytesOut = CudaPlotContext.ComputeFloatBufferBytes(numGrid);

            if (!CudaPlotContext.CheckCuda(CuDriver.CtxSetCurrent(CudaPlotContext.ContextHandle), "CtxSetCurrent"))
                return false;

            var slot = CudaPlotContext.RentSlot();
            if (slot == null)
                return false;

            bool poison = false;
            try
            {
                if (!slot.EnsureBuffers(bytesSamples, bytesGrid, bytesOut, 0))
                {
                    poison = true;
                    return false;
                }

                fixed (float* pSamples = samples, pGrid = grid, pOut = outDensity)
                {
                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D0, (IntPtr)pSamples, bytesSamples, slot.Stream), "MemcpyHtoDAsync(Samples)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D1, (IntPtr)pGrid, bytesGrid, slot.Stream), "MemcpyHtoDAsync(Grid)"))
                    {
                        poison = true;
                        return false;
                    }

                    IntPtr dSamples = slot.D0;
                    IntPtr dGrid = slot.D1;
                    IntPtr dOut = slot.D2;

                    void** pArgs = stackalloc void*[7];
                    pArgs[0] = &dSamples;
                    pArgs[1] = &dGrid;
                    pArgs[2] = &dOut;
                    pArgs[3] = &numSamples;
                    pArgs[4] = &numGrid;
                    pArgs[5] = &invH2;
                    pArgs[6] = &normFactor;

                    uint blockSize = 256;
                    uint gridSize = (uint)(((long)numGrid + blockSize - 1) / blockSize);

                    if (!CudaPlotContext.CheckCuda(
                        CuDriver.LaunchKernel(
                            CudaPlotContext.Kde1DFn,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, slot.Stream,
                            (IntPtr)pArgs,
                            IntPtr.Zero),
                        "LaunchKernel(statsviz_kde_evaluate_fp32)"))
                    {
                        poison = true;
                        return false;
                    }

                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pOut, slot.D2, bytesOut, slot.Stream), "MemcpyDtoHAsync(Out1D)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.StreamSynchronize(slot.Stream), "StreamSynchronize(Kde1D)"))
                    {
                        poison = true;
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                poison = true;
                Trace.TraceWarning($"Glacier.Plot CUDA Kde1D execution failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                CudaPlotContext.ReturnSlot(slot, poison);
            }
        }
        finally
        {
            CudaPlotContext.ExitNvidiaExecutionReadLock();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryExecuteGpuKde2D(
        ReadOnlySpan<float> xSamples,
        ReadOnlySpan<float> ySamples,
        ReadOnlySpan<float> xGrid,
        ReadOnlySpan<float> yGrid,
        Span<float> outDensity,
        int numSamples,
        int nx,
        int ny,
        int gridTotal,
        float invHx2,
        float invHy2,
        float normFactor)
    {
        if (!CudaPlotContext.TryEnterNvidiaExecutionReadLock())
            return false;

        try
        {
            if (CudaPlotContext.Kde2DFn == IntPtr.Zero)
                return false;

            nuint bytesSamples = CudaPlotContext.ComputeFloatBufferBytes(numSamples);
            nuint bytesXGrid = CudaPlotContext.ComputeFloatBufferBytes(nx);
            nuint bytesYGrid = CudaPlotContext.ComputeFloatBufferBytes(ny);
            nuint bytesOut = CudaPlotContext.ComputeFloatBufferBytes(gridTotal);

            if (!CudaPlotContext.CheckCuda(CuDriver.CtxSetCurrent(CudaPlotContext.ContextHandle), "CtxSetCurrent"))
                return false;

            var slot = CudaPlotContext.RentSlot();
            if (slot == null)
                return false;

            bool poison = false;
            try
            {
                if (!slot.EnsureBuffers(bytesSamples, bytesSamples, bytesXGrid, bytesYGrid, bytesOut))
                {
                    poison = true;
                    return false;
                }

                fixed (float* pXS = xSamples, pYS = ySamples, pXG = xGrid, pYG = yGrid, pOut = outDensity)
                {
                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D0, (IntPtr)pXS, bytesSamples, slot.Stream), "MemcpyHtoDAsync(XSamples)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D1, (IntPtr)pYS, bytesSamples, slot.Stream), "MemcpyHtoDAsync(YSamples)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D2, (IntPtr)pXG, bytesXGrid, slot.Stream), "MemcpyHtoDAsync(XGrid)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D3, (IntPtr)pYG, bytesYGrid, slot.Stream), "MemcpyHtoDAsync(YGrid)"))
                    {
                        poison = true;
                        return false;
                    }

                    IntPtr dXSamples = slot.D0;
                    IntPtr dYSamples = slot.D1;
                    IntPtr dXGrid = slot.D2;
                    IntPtr dYGrid = slot.D3;
                    IntPtr dOut = slot.D4;

                    void** pArgs = stackalloc void*[11];
                    pArgs[0] = &dXSamples;
                    pArgs[1] = &dYSamples;
                    pArgs[2] = &dXGrid;
                    pArgs[3] = &dYGrid;
                    pArgs[4] = &dOut;
                    pArgs[5] = &numSamples;
                    pArgs[6] = &nx;
                    pArgs[7] = &ny;
                    pArgs[8] = &invHx2;
                    pArgs[9] = &invHy2;
                    pArgs[10] = &normFactor;

                    uint blockSize = 256;
                    uint gridSize = (uint)(((long)gridTotal + blockSize - 1) / blockSize);

                    if (!CudaPlotContext.CheckCuda(
                        CuDriver.LaunchKernel(
                            CudaPlotContext.Kde2DFn,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, slot.Stream,
                            (IntPtr)pArgs,
                            IntPtr.Zero),
                        "LaunchKernel(statsviz_kde2d_evaluate_fp32)"))
                    {
                        poison = true;
                        return false;
                    }

                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pOut, slot.D4, bytesOut, slot.Stream), "MemcpyDtoHAsync(Out2D)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.StreamSynchronize(slot.Stream), "StreamSynchronize(Kde2D)"))
                    {
                        poison = true;
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                poison = true;
                Trace.TraceWarning($"Glacier.Plot CUDA Kde2D execution failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                CudaPlotContext.ReturnSlot(slot, poison);
            }
        }
        finally
        {
            CudaPlotContext.ExitNvidiaExecutionReadLock();
        }
    }

    #endregion
}
