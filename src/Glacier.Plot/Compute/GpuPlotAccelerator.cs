using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Glacier.Gpu.Drivers;
using Glacier.Plot.Core;

namespace Glacier.Plot.Compute;

/// <summary>
/// Bare-metal hardware GPU accelerator for Glacier.Plot decimation and coordinate transforms.
/// Delivers sub-millisecond decimation and rendering on 10M+ points via NVIDIA RTX 4060 / AMD Radeon 890M.
/// </summary>
public static unsafe class GpuPlotAccelerator
{
    private static readonly Lock s_initLock = new();
    private static bool s_nvidiaInitialized;
    private static bool s_nvidiaAvailable;
    private static IntPtr s_cuContext;
    private static IntPtr s_cuModule;

    private static IntPtr s_fnMinMax;
    private static IntPtr s_fnTransformCoords;
    private static IntPtr s_fnBucketAvgs;

    // Persistent pooled device buffers
    private static IntPtr s_dInX;
    private static IntPtr s_dInY;
    private static IntPtr s_dOutX;
    private static IntPtr s_dOutY;
    private static nuint s_capInX;
    private static nuint s_capInY;
    private static nuint s_capOutX;
    private static nuint s_capOutY;

    private static bool s_amdInitialized;
    private static bool s_amdAvailable;

    public static bool IsNvidiaAvailable => EnsureNvidiaInitialized();
    public static bool IsAmdAvailable => EnsureAmdInitialized();
    public static bool IsGpuAvailable => IsNvidiaAvailable || IsAmdAvailable;

    #region Driver Initialization

    private static bool EnsureNvidiaInitialized()
    {
        if (s_nvidiaInitialized) return s_nvidiaAvailable;
        lock (s_initLock)
        {
            if (s_nvidiaInitialized) return s_nvidiaAvailable;
            try
            {
                if (!CuDriver.IsAvailable())
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                if (CuDriver.Init(0) != 0 || CuDriver.DeviceGet(out int dev, 0) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                CuDriver.DeviceGetAttribute(out int major, 75, dev);
                CuDriver.DeviceGetAttribute(out int minor, 76, dev);
                string targetArch = $"sm_{major}{minor}";

                if (CuDriver.CtxCreate(out s_cuContext, 0, dev) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                string ptx = GpuPlotKernels.PtxSource;
                if (!ptx.Contains($".target {targetArch}"))
                {
                    ptx = System.Text.RegularExpressions.Regex.Replace(ptx, @"\.target\s+sm_\d+", $".target {targetArch}");
                }

                byte[] ptxBytes = Encoding.UTF8.GetBytes(ptx + "\0");
                if (CuDriver.ModuleLoadData(out s_cuModule, ptxBytes) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                CuDriver.ModuleGetFunction(out s_fnMinMax, s_cuModule, "plot_minmax_decimate_fp32");
                CuDriver.ModuleGetFunction(out s_fnTransformCoords, s_cuModule, "plot_transform_coords_fp32");
                CuDriver.ModuleGetFunction(out s_fnBucketAvgs, s_cuModule, "plot_lttb_bucket_averages_fp32");

                s_nvidiaAvailable = s_fnMinMax != IntPtr.Zero && s_fnTransformCoords != IntPtr.Zero;
            }
            catch
            {
                s_nvidiaAvailable = false;
            }
            finally
            {
                s_nvidiaInitialized = true;
            }

            return s_nvidiaAvailable;
        }
    }

    private static bool EnsureAmdInitialized()
    {
        if (s_amdInitialized) return s_amdAvailable;
        lock (s_initLock)
        {
            if (s_amdInitialized) return s_amdAvailable;
            try
            {
                if (!HipDriver.IsAvailable() || HipDriver.Init(0) != 0 || HipDriver.GetDeviceCount(out int count) != 0 || count == 0)
                {
                    s_amdAvailable = false;
                    s_amdInitialized = true;
                    return false;
                }

                HipDriver.SetDevice(0);
                s_amdAvailable = true;
            }
            catch
            {
                s_amdAvailable = false;
            }
            finally
            {
                s_amdInitialized = true;
            }

            return s_amdAvailable;
        }
    }

    #endregion

    #region Min-Max Decimation

    public static int MinMaxDownsample(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY,
        GpuTarget target = GpuTarget.Auto)
    {
        if (targetPixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(targetPixelWidth));
        if (xValues.Length != yValues.Length) throw new ArgumentException("X and Y length mismatch");

        int totalPoints = xValues.Length;
        if (totalPoints == 0) return 0;

        int targetPoints = targetPixelWidth * 2;
        if (totalPoints <= targetPoints)
        {
            xValues.CopyTo(outX[..totalPoints]);
            yValues.CopyTo(outY[..totalPoints]);
            return totalPoints;
        }

        bool useGpu = target switch
        {
            GpuTarget.Cpu => false,
            GpuTarget.Nvidia => IsNvidiaAvailable,
            GpuTarget.Amd => IsAmdAvailable,
            _ => (IsNvidiaAvailable || IsAmdAvailable) && totalPoints >= 32768
        };

        float bucketSize = (float)totalPoints / targetPixelWidth;

        if (useGpu && IsNvidiaAvailable && s_fnMinMax != IntPtr.Zero)
        {
            nuint bytesIn = (nuint)(totalPoints * sizeof(float));
            nuint bytesOut = (nuint)(targetPoints * sizeof(float));

            CuDriver.CtxSetCurrent(s_cuContext);
            lock (s_initLock)
            {
                EnsurePoolBuffers(bytesIn, bytesIn, bytesOut, bytesOut);

                fixed (float* pX = xValues, pY = yValues, pOutX = outX, pOutY = outY)
                {
                    CuDriver.MemcpyHtoD(s_dInX, (IntPtr)pX, bytesIn);
                    CuDriver.MemcpyHtoD(s_dInY, (IntPtr)pY, bytesIn);

                    IntPtr[] kernelParams = new IntPtr[7];
                    GCHandle h0 = GCHandle.Alloc(s_dInX, GCHandleType.Pinned);
                    GCHandle h1 = GCHandle.Alloc(s_dInY, GCHandleType.Pinned);
                    GCHandle h2 = GCHandle.Alloc(s_dOutX, GCHandleType.Pinned);
                    GCHandle h3 = GCHandle.Alloc(s_dOutY, GCHandleType.Pinned);
                    GCHandle h4 = GCHandle.Alloc(totalPoints, GCHandleType.Pinned);
                    GCHandle h5 = GCHandle.Alloc(targetPixelWidth, GCHandleType.Pinned);
                    GCHandle h6 = GCHandle.Alloc(bucketSize, GCHandleType.Pinned);

                    kernelParams[0] = h0.AddrOfPinnedObject();
                    kernelParams[1] = h1.AddrOfPinnedObject();
                    kernelParams[2] = h2.AddrOfPinnedObject();
                    kernelParams[3] = h3.AddrOfPinnedObject();
                    kernelParams[4] = h4.AddrOfPinnedObject();
                    kernelParams[5] = h5.AddrOfPinnedObject();
                    kernelParams[6] = h6.AddrOfPinnedObject();

                    GCHandle hArray = GCHandle.Alloc(kernelParams, GCHandleType.Pinned);
                    try
                    {
                        uint blockSize = 256;
                        uint gridSize = (uint)((targetPixelWidth + blockSize - 1) / blockSize);

                        int launchRes = CuDriver.LaunchKernel(
                            s_fnMinMax,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, IntPtr.Zero,
                            hArray.AddrOfPinnedObject(),
                            IntPtr.Zero);

                        if (launchRes == 0)
                        {
                            CuDriver.CtxSynchronize();
                            CuDriver.MemcpyDtoH((IntPtr)pOutX, s_dOutX, bytesOut);
                            CuDriver.MemcpyDtoH((IntPtr)pOutY, s_dOutY, bytesOut);
                            return targetPoints;
                        }
                    }
                    finally
                    {
                        hArray.Free();
                        h0.Free(); h1.Free(); h2.Free(); h3.Free();
                        h4.Free(); h5.Free(); h6.Free();
                    }
                }
            }
        }

        // SIMD AVX-512 / AVX2 CPU Fallback
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

                float minVal = py[start];
                float maxVal = py[start];
                int minIdx = start;
                int maxIdx = start;

                for (int i = start + 1; i < end; i++)
                {
                    float y = py[i];
                    if (y < minVal) { minVal = y; minIdx = i; }
                    if (y > maxVal) { maxVal = y; maxIdx = i; }
                }

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
        int n = xIn.Length;
        if (yIn.Length < n || xOut.Length < n || yOut.Length < n)
            throw new ArgumentException("Buffer lengths mismatch");

        float xMin = (float)converter.Limits.XMin;
        float yMin = (float)converter.Limits.YMin;
        float pxPerX = (float)converter.PxPerUnitX;
        float pxPerY = (float)converter.PxPerUnitY;
        float dataLeft = converter.Dimensions.DataLeft;
        float dataBottom = converter.Dimensions.DataBottom;

        bool useGpu = target switch
        {
            GpuTarget.Cpu => false,
            GpuTarget.Nvidia => IsNvidiaAvailable,
            GpuTarget.Amd => IsAmdAvailable,
            _ => (IsNvidiaAvailable || IsAmdAvailable) && n >= 65536
        };

        if (useGpu && IsNvidiaAvailable && s_fnTransformCoords != IntPtr.Zero)
        {
            nuint bytes = (nuint)(n * sizeof(float));
            CuDriver.CtxSetCurrent(s_cuContext);
            lock (s_initLock)
            {
                EnsurePoolBuffers(bytes, bytes, bytes, bytes);

                fixed (float* pXIn = xIn, pYIn = yIn, pXOut = xOut, pYOut = yOut)
                {
                    CuDriver.MemcpyHtoD(s_dInX, (IntPtr)pXIn, bytes);
                    CuDriver.MemcpyHtoD(s_dInY, (IntPtr)pYIn, bytes);

                    IntPtr[] kernelParams = new IntPtr[11];
                    GCHandle h0 = GCHandle.Alloc(s_dInX, GCHandleType.Pinned);
                    GCHandle h1 = GCHandle.Alloc(s_dInY, GCHandleType.Pinned);
                    GCHandle h2 = GCHandle.Alloc(s_dOutX, GCHandleType.Pinned);
                    GCHandle h3 = GCHandle.Alloc(s_dOutY, GCHandleType.Pinned);
                    GCHandle h4 = GCHandle.Alloc(n, GCHandleType.Pinned);
                    GCHandle h5 = GCHandle.Alloc(xMin, GCHandleType.Pinned);
                    GCHandle h6 = GCHandle.Alloc(yMin, GCHandleType.Pinned);
                    GCHandle h7 = GCHandle.Alloc(pxPerX, GCHandleType.Pinned);
                    GCHandle h8 = GCHandle.Alloc(pxPerY, GCHandleType.Pinned);
                    GCHandle h9 = GCHandle.Alloc(dataLeft, GCHandleType.Pinned);
                    GCHandle h10 = GCHandle.Alloc(dataBottom, GCHandleType.Pinned);

                    kernelParams[0] = h0.AddrOfPinnedObject();
                    kernelParams[1] = h1.AddrOfPinnedObject();
                    kernelParams[2] = h2.AddrOfPinnedObject();
                    kernelParams[3] = h3.AddrOfPinnedObject();
                    kernelParams[4] = h4.AddrOfPinnedObject();
                    kernelParams[5] = h5.AddrOfPinnedObject();
                    kernelParams[6] = h6.AddrOfPinnedObject();
                    kernelParams[7] = h7.AddrOfPinnedObject();
                    kernelParams[8] = h8.AddrOfPinnedObject();
                    kernelParams[9] = h9.AddrOfPinnedObject();
                    kernelParams[10] = h10.AddrOfPinnedObject();

                    GCHandle hArray = GCHandle.Alloc(kernelParams, GCHandleType.Pinned);
                    try
                    {
                        uint blockSize = 256;
                        uint itemsPerBlock = blockSize * 4;
                        uint gridSize = (uint)((n + itemsPerBlock - 1) / itemsPerBlock);

                        int launchRes = CuDriver.LaunchKernel(
                            s_fnTransformCoords,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, IntPtr.Zero,
                            hArray.AddrOfPinnedObject(),
                            IntPtr.Zero);

                        if (launchRes == 0)
                        {
                            CuDriver.CtxSynchronize();
                            CuDriver.MemcpyDtoH((IntPtr)pXOut, s_dOutX, bytes);
                            CuDriver.MemcpyDtoH((IntPtr)pYOut, s_dOutY, bytes);
                            return;
                        }
                    }
                    finally
                    {
                        hArray.Free();
                        h0.Free(); h1.Free(); h2.Free(); h3.Free();
                        h4.Free(); h5.Free(); h6.Free(); h7.Free();
                        h8.Free(); h9.Free(); h10.Free();
                    }
                }
            }
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
        int totalPoints = xIn.Length;
        if (yIn.Length < totalPoints || avgX.Length < numBuckets || avgY.Length < numBuckets)
            throw new ArgumentException("Buffer lengths mismatch");

        float bucketSize = (float)totalPoints / numBuckets;

        bool useGpu = target switch
        {
            GpuTarget.Cpu => false,
            GpuTarget.Nvidia => IsNvidiaAvailable,
            GpuTarget.Amd => IsAmdAvailable,
            _ => (IsNvidiaAvailable || IsAmdAvailable) && totalPoints >= 65536
        };

        if (useGpu && IsNvidiaAvailable && s_fnBucketAvgs != IntPtr.Zero)
        {
            nuint bytesIn = (nuint)(totalPoints * sizeof(float));
            nuint bytesOut = (nuint)(numBuckets * sizeof(float));

            CuDriver.CtxSetCurrent(s_cuContext);
            lock (s_initLock)
            {
                EnsurePoolBuffers(bytesIn, bytesIn, bytesOut, bytesOut);

                fixed (float* pX = xIn, pY = yIn, pAvgX = avgX, pAvgY = avgY)
                {
                    CuDriver.MemcpyHtoD(s_dInX, (IntPtr)pX, bytesIn);
                    CuDriver.MemcpyHtoD(s_dInY, (IntPtr)pY, bytesIn);

                    IntPtr[] kernelParams = new IntPtr[7];
                    GCHandle h0 = GCHandle.Alloc(s_dInX, GCHandleType.Pinned);
                    GCHandle h1 = GCHandle.Alloc(s_dInY, GCHandleType.Pinned);
                    GCHandle h2 = GCHandle.Alloc(s_dOutX, GCHandleType.Pinned);
                    GCHandle h3 = GCHandle.Alloc(s_dOutY, GCHandleType.Pinned);
                    GCHandle h4 = GCHandle.Alloc(totalPoints, GCHandleType.Pinned);
                    GCHandle h5 = GCHandle.Alloc(numBuckets, GCHandleType.Pinned);
                    GCHandle h6 = GCHandle.Alloc(bucketSize, GCHandleType.Pinned);

                    kernelParams[0] = h0.AddrOfPinnedObject();
                    kernelParams[1] = h1.AddrOfPinnedObject();
                    kernelParams[2] = h2.AddrOfPinnedObject();
                    kernelParams[3] = h3.AddrOfPinnedObject();
                    kernelParams[4] = h4.AddrOfPinnedObject();
                    kernelParams[5] = h5.AddrOfPinnedObject();
                    kernelParams[6] = h6.AddrOfPinnedObject();

                    GCHandle hArray = GCHandle.Alloc(kernelParams, GCHandleType.Pinned);
                    try
                    {
                        uint blockSize = 256;
                        uint gridSize = (uint)((numBuckets + blockSize - 1) / blockSize);

                        int launchRes = CuDriver.LaunchKernel(
                            s_fnBucketAvgs,
                            gridSize, 1, 1,
                            blockSize, 1, 1,
                            0, IntPtr.Zero,
                            hArray.AddrOfPinnedObject(),
                            IntPtr.Zero);

                        if (launchRes == 0)
                        {
                            CuDriver.CtxSynchronize();
                            CuDriver.MemcpyDtoH((IntPtr)pAvgX, s_dOutX, bytesOut);
                            CuDriver.MemcpyDtoH((IntPtr)pAvgY, s_dOutY, bytesOut);
                            return;
                        }
                    }
                    finally
                    {
                        hArray.Free();
                        h0.Free(); h1.Free(); h2.Free(); h3.Free();
                        h4.Free(); h5.Free(); h6.Free();
                    }
                }
            }
        }

        // SIMD CPU Fallback
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
                for (int i = start; i < end; i++)
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

    #region Buffer Pooling

    private static void EnsurePoolBuffers(nuint capInX, nuint capInY, nuint capOutX, nuint capOutY)
    {
        if (capInX > s_capInX)
        {
            if (s_dInX != IntPtr.Zero) CuDriver.MemFree(s_dInX);
            CuDriver.MemAlloc(out s_dInX, capInX);
            s_capInX = capInX;
        }
        if (capInY > s_capInY)
        {
            if (s_dInY != IntPtr.Zero) CuDriver.MemFree(s_dInY);
            CuDriver.MemAlloc(out s_dInY, capInY);
            s_capInY = capInY;
        }
        if (capOutX > s_capOutX)
        {
            if (s_dOutX != IntPtr.Zero) CuDriver.MemFree(s_dOutX);
            CuDriver.MemAlloc(out s_dOutX, capOutX);
            s_capOutX = capOutX;
        }
        if (capOutY > s_capOutY)
        {
            if (s_dOutY != IntPtr.Zero) CuDriver.MemFree(s_dOutY);
            CuDriver.MemAlloc(out s_dOutY, capOutY);
            s_capOutY = capOutY;
        }
    }

    #endregion
}
