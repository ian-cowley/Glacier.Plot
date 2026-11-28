using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;
using Glacier.Gpu.Drivers;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;

namespace Glacier.Plot.Compute;

/// <summary>
/// Hardware GPU and SIMD CPU accelerator for Glacier.Plot decimation and coordinate transforms.
/// Executes CUDA PTX compute kernels via <see cref="CudaPlotContext"/> on NVIDIA GPUs
/// and multi-core AVX-512 / AVX2 SIMD kernels on CPU hosts.
/// </summary>
public static unsafe class GpuPlotAccelerator
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
    /// Safe to call multiple times; subsequent GPU calls will re-initialize CUDA cleanly.
    /// </summary>
    public static void ReleaseGpuResources() => CudaPlotContext.ReleaseGpuResources();

    #region Min-Max Decimation

    public static int MinMaxDownsample(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY,
        GpuTarget target = GpuTarget.Auto)
    {
        CudaPlotContext.ValidateTarget(target);
        if (targetPixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(targetPixelWidth));
        if (xValues.Length != yValues.Length) throw new ArgumentException("X and Y length mismatch");

        int totalPoints = xValues.Length;
        if (totalPoints == 0) return 0;

        int targetPoints = checked(targetPixelWidth * 2);
        if (totalPoints <= targetPoints)
        {
            if (outX.Length < totalPoints || outY.Length < totalPoints)
                throw new ArgumentException("Output buffers too small.");
            xValues.CopyTo(outX[..totalPoints]);
            yValues.CopyTo(outY[..totalPoints]);
            return totalPoints;
        }

        if (outX.Length < targetPoints || outY.Length < targetPoints)
            throw new ArgumentException("Output buffers too small.");

        float bucketSize = (float)totalPoints / targetPixelWidth;

        if (target == GpuTarget.Nvidia)
        {
            if (TryExecuteGpuMinMax(xValues, yValues, targetPixelWidth, outX, outY, bucketSize, totalPoints, targetPoints))
                return targetPoints;

            if (!IsNvidiaAvailable)
            {
                throw new PlatformNotSupportedException(
                    "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
            }

            throw new InvalidOperationException(
                "NVIDIA CUDA execution failed for GpuPlotAccelerator.MinMaxDownsample.");
        }

        if (target == GpuTarget.Auto && IsNvidiaAvailable && totalPoints >= 32768 &&
            TryExecuteGpuMinMax(xValues, yValues, targetPixelWidth, outX, outY, bucketSize, totalPoints, targetPoints))
        {
            return targetPoints;
        }

        return MinMaxKernelsFallback(xValues, yValues, targetPixelWidth, outX, outY, bucketSize, totalPoints);
    }

    private static int MinMaxKernelsFallback(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY,
        float bucketSize,
        int totalPoints)
    {
        fixed (float* pX = xValues, pY = yValues, pOutX = outX, pOutY = outY)
        {
            float* px = pX; float* py = pY; float* pox = pOutX; float* poy = pOutY;
            Parallel.For(0, targetPixelWidth, bucket =>
            {
                int start = (int)(bucket * bucketSize);
                int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
                if (start >= end) return;

                MinMaxKernels.FindBucketMinMax(py, start, end, out float minVal, out float maxVal, out int minIdx, out int maxIdx);

                int outIdx = bucket * 2;
                if (minIdx <= maxIdx)
                {
                    pox[outIdx] = px[minIdx];
                    poy[outIdx] = minVal;
                    pox[outIdx + 1] = px[maxIdx];
                    poy[outIdx + 1] = maxVal;
                }
                else
                {
                    pox[outIdx] = px[maxIdx];
                    poy[outIdx] = maxVal;
                    pox[outIdx + 1] = px[minIdx];
                    poy[outIdx + 1] = minVal;
                }
            });
        }
        return targetPixelWidth * 2;
    }

    #endregion

    #region 2D Coordinate Transformation

    public static void TransformCoordinates(
        ReadOnlySpan<float> xIn,
        ReadOnlySpan<float> yIn,
        Span<float> xOut,
        Span<float> yOut,
        in CoordinateConverter converter,
        GpuTarget target = GpuTarget.Auto)
    {
        CudaPlotContext.ValidateTarget(target);
        int n = xIn.Length;
        if (yIn.Length < n || xOut.Length < n || yOut.Length < n)
            throw new ArgumentException("Buffer lengths mismatch");
        if (n == 0) return;

        float xMin = (float)converter.Limits.XMin;
        float yMin = (float)converter.Limits.YMin;
        float pxPerX = (float)converter.PxPerUnitX;
        float pxPerY = (float)converter.PxPerUnitY;
        float dataLeft = converter.Dimensions.DataLeft;
        float dataBottom = converter.Dimensions.DataBottom;

        if (target == GpuTarget.Nvidia)
        {
            if (TryExecuteGpuTransformCoords(xIn, yIn, xOut, yOut, n, xMin, yMin, pxPerX, pxPerY, dataLeft, dataBottom))
                return;

            if (!IsNvidiaAvailable)
            {
                throw new PlatformNotSupportedException(
                    "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
            }

            throw new InvalidOperationException(
                "NVIDIA CUDA execution failed for GpuPlotAccelerator.TransformCoordinates.");
        }

        if (target == GpuTarget.Auto && IsNvidiaAvailable && n >= 65536 &&
            TryExecuteGpuTransformCoords(xIn, yIn, xOut, yOut, n, xMin, yMin, pxPerX, pxPerY, dataLeft, dataBottom))
        {
            return;
        }

        // SIMD AVX-512 / AVX2 CPU Fallback
        fixed (float* pXIn = xIn, pYIn = yIn, pXOut = xOut, pYOut = yOut)
        {
            float* pxin = pXIn; float* pyin = pYIn; float* pxout = pXOut; float* pyout = pYOut;
            Parallel.For(0, (n + 1023) / 1024, chunk =>
            {
                int start = chunk * 1024;
                int end = Math.Min(start + 1024, n);
                int i = start;

                if (Vector512.IsHardwareAccelerated && end - i >= Vector512<float>.Count)
                {
                    int step = Vector512<float>.Count;
                    int limit = end - step;
                    var vxMin = Vector512.Create(xMin);
                    var vyMin = Vector512.Create(yMin);
                    var vpxPerX = Vector512.Create(pxPerX);
                    var vpxPerY = Vector512.Create(pxPerY);
                    var vdataLeft = Vector512.Create(dataLeft);
                    var vdataBottom = Vector512.Create(dataBottom);

                    while (i <= limit)
                    {
                        var vx = Vector512.Load(pxin + i);
                        var vy = Vector512.Load(pyin + i);
                        var rx = Vector512.FusedMultiplyAdd(vx - vxMin, vpxPerX, vdataLeft);
                        var ry = vdataBottom - (vy - vyMin) * vpxPerY;
                        rx.Store(pxout + i);
                        ry.Store(pyout + i);
                        i += step;
                    }
                }
                else if (Vector256.IsHardwareAccelerated && end - i >= Vector256<float>.Count)
                {
                    int step = Vector256<float>.Count;
                    int limit = end - step;
                    var vxMin = Vector256.Create(xMin);
                    var vyMin = Vector256.Create(yMin);
                    var vpxPerX = Vector256.Create(pxPerX);
                    var vpxPerY = Vector256.Create(pxPerY);
                    var vdataLeft = Vector256.Create(dataLeft);
                    var vdataBottom = Vector256.Create(dataBottom);

                    while (i <= limit)
                    {
                        var vx = Vector256.Load(pxin + i);
                        var vy = Vector256.Load(pyin + i);
                        var rx = (vx - vxMin) * vpxPerX + vdataLeft;
                        var ry = vdataBottom - (vy - vyMin) * vpxPerY;
                        rx.Store(pxout + i);
                        ry.Store(pyout + i);
                        i += step;
                    }
                }

                for (; i < end; i++)
                {
                    pxout[i] = dataLeft + (pxin[i] - xMin) * pxPerX;
                    pyout[i] = dataBottom - (pyin[i] - yMin) * pxPerY;
                }
            });
        }
    }

    #endregion

    #region Bucket Averages for LTTB

    public static void ComputeBucketAverages(
        ReadOnlySpan<float> xIn,
        ReadOnlySpan<float> yIn,
        int numBuckets,
        Span<float> avgX,
        Span<float> avgY,
        GpuTarget target = GpuTarget.Auto)
    {
        CudaPlotContext.ValidateTarget(target);
        if (numBuckets <= 0) throw new ArgumentOutOfRangeException(nameof(numBuckets));

        int totalPoints = xIn.Length;
        if (yIn.Length < totalPoints || avgX.Length < numBuckets || avgY.Length < numBuckets)
            throw new ArgumentException("Buffer lengths mismatch");
        if (totalPoints == 0)
        {
            avgX[..numBuckets].Clear();
            avgY[..numBuckets].Clear();
            return;
        }

        float bucketSize = (float)totalPoints / numBuckets;

        if (target == GpuTarget.Nvidia)
        {
            if (TryExecuteGpuBucketAvgs(xIn, yIn, numBuckets, avgX, avgY, totalPoints, bucketSize))
                return;

            if (!IsNvidiaAvailable)
            {
                throw new PlatformNotSupportedException(
                    "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
            }

            throw new InvalidOperationException(
                "NVIDIA CUDA execution failed for GpuPlotAccelerator.ComputeBucketAverages.");
        }

        if (target == GpuTarget.Auto && IsNvidiaAvailable && totalPoints >= 65536 &&
            TryExecuteGpuBucketAvgs(xIn, yIn, numBuckets, avgX, avgY, totalPoints, bucketSize))
        {
            return;
        }

        // SIMD AVX-512 / AVX2 CPU Fallback with double-precision widened accumulation
        fixed (float* pX = xIn, pY = yIn, pAvgX = avgX, pAvgY = avgY)
        {
            float* px = pX; float* py = pY; float* pax = pAvgX; float* pay = pAvgY;
            Parallel.For(0, numBuckets, b =>
            {
                int start = (int)(b * bucketSize);
                int end = Math.Min((int)((b + 1) * bucketSize), totalPoints);
                int count = end - start;
                if (count <= 0)
                {
                    pax[b] = 0f;
                    pay[b] = 0f;
                    return;
                }

                double sumX = 0.0;
                double sumY = 0.0;
                int i = start;

                if (Vector512.IsHardwareAccelerated && count >= 16)
                {
                    var vSumX0 = Vector512<double>.Zero;
                    var vSumX1 = Vector512<double>.Zero;
                    var vSumY0 = Vector512<double>.Zero;
                    var vSumY1 = Vector512<double>.Zero;
                    int limit = end - 16;
                    for (; i <= limit; i += 16)
                    {
                        var (lx, ux) = Vector512.Widen(Vector512.Load(px + i));
                        var (ly, uy) = Vector512.Widen(Vector512.Load(py + i));
                        vSumX0 += lx;
                        vSumX1 += ux;
                        vSumY0 += ly;
                        vSumY1 += uy;
                    }
                    sumX += Vector512.Sum(vSumX0 + vSumX1);
                    sumY += Vector512.Sum(vSumY0 + vSumY1);
                }
                else if (Vector256.IsHardwareAccelerated && count >= 8)
                {
                    var vSumX0 = Vector256<double>.Zero;
                    var vSumX1 = Vector256<double>.Zero;
                    var vSumY0 = Vector256<double>.Zero;
                    var vSumY1 = Vector256<double>.Zero;
                    int limit = end - 8;
                    for (; i <= limit; i += 8)
                    {
                        var (lx, ux) = Vector256.Widen(Vector256.Load(px + i));
                        var (ly, uy) = Vector256.Widen(Vector256.Load(py + i));
                        vSumX0 += lx;
                        vSumX1 += ux;
                        vSumY0 += ly;
                        vSumY1 += uy;
                    }
                    sumX += Vector256.Sum(vSumX0 + vSumX1);
                    sumY += Vector256.Sum(vSumY0 + vSumY1);
                }

                for (; i < end; i++)
                {
                    sumX += px[i];
                    sumY += py[i];
                }
                pax[b] = (float)(sumX / count);
                pay[b] = (float)(sumY / count);
            });
        }
    }

    #endregion

    #region CUDA Dispatch

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryExecuteGpuMinMax(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY,
        float bucketSize,
        int totalPoints,
        int targetPoints)
    {
        if (!CudaPlotContext.TryEnterNvidiaExecutionReadLock())
            return false;

        try
        {
            if (CudaPlotContext.MinMaxFn == IntPtr.Zero)
                return false;

            nuint bytesIn = CudaPlotContext.ComputeFloatBufferBytes(totalPoints);
            nuint bytesOut = CudaPlotContext.ComputeFloatBufferBytes(targetPoints);

            if (!CudaPlotContext.CheckCuda(CuDriver.CtxSetCurrent(CudaPlotContext.ContextHandle), "CtxSetCurrent"))
                return false;

            var slot = CudaPlotContext.RentSlot();
            if (slot == null)
                return false;

            bool poison = false;
            try
            {
                if (!slot.EnsureBuffers(bytesIn, bytesIn, bytesOut, bytesOut))
                {
                    poison = true;
                    return false;
                }

                fixed (float* pX = xValues, pY = yValues, pOutX = outX, pOutY = outY)
                {
                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D0, (IntPtr)pX, bytesIn, slot.Stream), "MemcpyHtoDAsync(X)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D1, (IntPtr)pY, bytesIn, slot.Stream), "MemcpyHtoDAsync(Y)"))
                    {
                        poison = true;
                        return false;
                    }

                    IntPtr dInX = slot.D0;
                    IntPtr dInY = slot.D1;
                    IntPtr dOutX = slot.D2;
                    IntPtr dOutY = slot.D3;

                    void** pArgs = stackalloc void*[7];
                    pArgs[0] = &dInX;
                    pArgs[1] = &dInY;
                    pArgs[2] = &dOutX;
                    pArgs[3] = &dOutY;
                    pArgs[4] = &totalPoints;
                    pArgs[5] = &targetPixelWidth;
                    pArgs[6] = &bucketSize;

                    uint blockSize = 256;
                    uint gridSize = (uint)(((long)targetPixelWidth + blockSize - 1) / blockSize);

                    if (!CudaPlotContext.CheckCuda(
                        CuDriver.LaunchKernel(
                            CudaPlotContext.MinMaxFn,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, slot.Stream,
                            (IntPtr)pArgs,
                            IntPtr.Zero),
                        "LaunchKernel(plot_minmax_decimate_fp32)"))
                    {
                        poison = true;
                        return false;
                    }

                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pOutX, slot.D2, bytesOut, slot.Stream), "MemcpyDtoHAsync(X)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pOutY, slot.D3, bytesOut, slot.Stream), "MemcpyDtoHAsync(Y)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.StreamSynchronize(slot.Stream), "StreamSynchronize(MinMax)"))
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
                Trace.TraceWarning($"Glacier.Plot CUDA MinMax execution failed: {ex.GetType().Name}: {ex.Message}");
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
    private static bool TryExecuteGpuTransformCoords(
        ReadOnlySpan<float> xIn,
        ReadOnlySpan<float> yIn,
        Span<float> xOut,
        Span<float> yOut,
        int n,
        float xMin,
        float yMin,
        float pxPerX,
        float pxPerY,
        float dataLeft,
        float dataBottom)
    {
        if (!CudaPlotContext.TryEnterNvidiaExecutionReadLock())
            return false;

        try
        {
            if (CudaPlotContext.TransformCoordsFn == IntPtr.Zero)
                return false;

            nuint bytes = CudaPlotContext.ComputeFloatBufferBytes(n);
            if (!CudaPlotContext.CheckCuda(CuDriver.CtxSetCurrent(CudaPlotContext.ContextHandle), "CtxSetCurrent"))
                return false;

            var slot = CudaPlotContext.RentSlot();
            if (slot == null)
                return false;

            bool poison = false;
            try
            {
                if (!slot.EnsureBuffers(bytes, bytes, bytes, bytes))
                {
                    poison = true;
                    return false;
                }

                fixed (float* pXIn = xIn, pYIn = yIn, pXOut = xOut, pYOut = yOut)
                {
                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D0, (IntPtr)pXIn, bytes, slot.Stream), "MemcpyHtoDAsync(XIn)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D1, (IntPtr)pYIn, bytes, slot.Stream), "MemcpyHtoDAsync(YIn)"))
                    {
                        poison = true;
                        return false;
                    }

                    IntPtr dInX = slot.D0;
                    IntPtr dInY = slot.D1;
                    IntPtr dOutX = slot.D2;
                    IntPtr dOutY = slot.D3;

                    void** pArgs = stackalloc void*[11];
                    pArgs[0] = &dInX;
                    pArgs[1] = &dInY;
                    pArgs[2] = &dOutX;
                    pArgs[3] = &dOutY;
                    pArgs[4] = &n;
                    pArgs[5] = &xMin;
                    pArgs[6] = &yMin;
                    pArgs[7] = &pxPerX;
                    pArgs[8] = &pxPerY;
                    pArgs[9] = &dataLeft;
                    pArgs[10] = &dataBottom;

                    uint blockSize = 256;
                    uint itemsPerBlock = blockSize * 4;
                    uint gridSize = (uint)(((long)n + itemsPerBlock - 1) / itemsPerBlock);

                    if (!CudaPlotContext.CheckCuda(
                        CuDriver.LaunchKernel(
                            CudaPlotContext.TransformCoordsFn,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, slot.Stream,
                            (IntPtr)pArgs,
                            IntPtr.Zero),
                        "LaunchKernel(plot_transform_coords_fp32)"))
                    {
                        poison = true;
                        return false;
                    }

                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pXOut, slot.D2, bytes, slot.Stream), "MemcpyDtoHAsync(XOut)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pYOut, slot.D3, bytes, slot.Stream), "MemcpyDtoHAsync(YOut)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.StreamSynchronize(slot.Stream), "StreamSynchronize(TransformCoords)"))
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
                Trace.TraceWarning($"Glacier.Plot CUDA TransformCoordinates execution failed: {ex.GetType().Name}: {ex.Message}");
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
    private static bool TryExecuteGpuBucketAvgs(
        ReadOnlySpan<float> xIn,
        ReadOnlySpan<float> yIn,
        int numBuckets,
        Span<float> avgX,
        Span<float> avgY,
        int totalPoints,
        float bucketSize)
    {
        if (!CudaPlotContext.TryEnterNvidiaExecutionReadLock())
            return false;

        try
        {
            if (CudaPlotContext.BucketAvgsFn == IntPtr.Zero)
                return false;

            nuint bytesIn = CudaPlotContext.ComputeFloatBufferBytes(totalPoints);
            nuint bytesOut = CudaPlotContext.ComputeFloatBufferBytes(numBuckets);

            if (!CudaPlotContext.CheckCuda(CuDriver.CtxSetCurrent(CudaPlotContext.ContextHandle), "CtxSetCurrent"))
                return false;

            var slot = CudaPlotContext.RentSlot();
            if (slot == null)
                return false;

            bool poison = false;
            try
            {
                if (!slot.EnsureBuffers(bytesIn, bytesIn, bytesOut, bytesOut))
                {
                    poison = true;
                    return false;
                }

                fixed (float* pX = xIn, pY = yIn, pAvgX = avgX, pAvgY = avgY)
                {
                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D0, (IntPtr)pX, bytesIn, slot.Stream), "MemcpyHtoDAsync(X)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyHtoDAsync(slot.D1, (IntPtr)pY, bytesIn, slot.Stream), "MemcpyHtoDAsync(Y)"))
                    {
                        poison = true;
                        return false;
                    }

                    IntPtr dInX = slot.D0;
                    IntPtr dInY = slot.D1;
                    IntPtr dOutX = slot.D2;
                    IntPtr dOutY = slot.D3;

                    void** pArgs = stackalloc void*[7];
                    pArgs[0] = &dInX;
                    pArgs[1] = &dInY;
                    pArgs[2] = &dOutX;
                    pArgs[3] = &dOutY;
                    pArgs[4] = &totalPoints;
                    pArgs[5] = &numBuckets;
                    pArgs[6] = &bucketSize;

                    uint blockSize = 256;
                    uint gridSize = (uint)(((long)numBuckets + blockSize - 1) / blockSize);

                    if (!CudaPlotContext.CheckCuda(
                        CuDriver.LaunchKernel(
                            CudaPlotContext.BucketAvgsFn,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, slot.Stream,
                            (IntPtr)pArgs,
                            IntPtr.Zero),
                        "LaunchKernel(plot_lttb_bucket_averages_fp32)"))
                    {
                        poison = true;
                        return false;
                    }

                    if (!CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pAvgX, slot.D2, bytesOut, slot.Stream), "MemcpyDtoHAsync(AvgX)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.MemcpyDtoHAsync((IntPtr)pAvgY, slot.D3, bytesOut, slot.Stream), "MemcpyDtoHAsync(AvgY)") ||
                        !CudaPlotContext.CheckCuda(CuDriver.StreamSynchronize(slot.Stream), "StreamSynchronize(BucketAvgs)"))
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
                Trace.TraceWarning($"Glacier.Plot CUDA ComputeBucketAverages execution failed: {ex.GetType().Name}: {ex.Message}");
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
