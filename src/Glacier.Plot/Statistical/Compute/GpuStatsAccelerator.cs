using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Glacier.Gpu.Drivers;
using Glacier.Plot.Statistical.Core;
using Glacier.Plot.Statistical.Kernels;

namespace Glacier.Plot.Statistical.Compute;

/// <summary>
/// Bare-metal GPU hardware accelerator for Glacier.StatsViz statistical kernels.
/// Accelerates Kernel Density Estimation (KDE) and high-dimensional distribution computations
/// via NVIDIA RTX 4060 dGPU, AMD Radeon 890M APU, or multi-threaded AVX-512 CPU.
/// </summary>
public static unsafe class GpuStatsAccelerator
{
    private static readonly Lock s_initLock = new();
    private static bool s_nvidiaInitialized;
    private static bool s_nvidiaAvailable;
    private static IntPtr s_cuContext;
    private static IntPtr s_cuModule;

    private static IntPtr s_fnKde1D;
    private static IntPtr s_fnKde2D;

    /// <summary>Gets whether NVIDIA CUDA PTX hardware acceleration is initialized and available.</summary>
    public static bool IsNvidiaAvailable => EnsureNvidiaInitialized();

    /// <summary>
    /// Gets whether AMD HIP GPU kernel offload is available.
    /// Currently false because embedded compute kernels use NVVM PTX; AMD/Intel/ARM hosts execute multi-core AVX-512/AVX2/AdvSimd SIMD kernels.
    /// </summary>
    public static bool IsAmdAvailable => false;

    /// <summary>Gets whether bare-metal GPU kernel acceleration is available.</summary>
    public static bool IsGpuAvailable => IsNvidiaAvailable;

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

                string ptx = GpuStatsKernels.PtxSource;
                if (!ptx.Contains($".target {targetArch}", StringComparison.Ordinal))
                {
                    ptx = ptx.Replace(".target sm_89", $".target {targetArch}", StringComparison.Ordinal);
                }

                byte[] ptxBytes = Encoding.UTF8.GetBytes(ptx + "\0");
                if (CuDriver.ModuleLoadData(out s_cuModule, ptxBytes) != 0)
                {
                    s_nvidiaAvailable = false;
                    s_nvidiaInitialized = true;
                    return false;
                }

                CuDriver.ModuleGetFunction(out s_fnKde1D, s_cuModule, "statsviz_kde_evaluate_fp32");
                CuDriver.ModuleGetFunction(out s_fnKde2D, s_cuModule, "statsviz_kde2d_evaluate_fp32");

                s_nvidiaAvailable = s_fnKde1D != IntPtr.Zero;
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

    #endregion

    #region Kernel Density Estimation (1D)

    public static void EvaluateKde(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        float bandwidth,
        Span<float> outDensity,
        GpuTarget target = GpuTarget.Auto)
    {
        if (bandwidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandwidth), "Bandwidth must be positive.");

        int numSamples = samples.Length;
        int numGrid = grid.Length;
        if (numSamples == 0 || numGrid == 0) return;
        if (outDensity.Length < numGrid)
            throw new ArgumentException("Output buffer too small.");

        float invH2 = 1.0f / (2.0f * bandwidth * bandwidth);
        float normFactor = 1.0f / (numSamples * bandwidth * MathF.Sqrt(2.0f * MathF.PI));

        long workItems = (long)numSamples * numGrid;
        bool useGpu = target switch
        {
            GpuTarget.Cpu => false,
            GpuTarget.Nvidia => IsNvidiaAvailable,
            GpuTarget.Amd => IsAmdAvailable,
            _ => (IsNvidiaAvailable || IsAmdAvailable) && workItems >= 32768
        };

        if (useGpu && IsNvidiaAvailable && s_fnKde1D != IntPtr.Zero)
        {
            if (TryExecuteGpuKde1D(samples, grid, outDensity, numSamples, numGrid, invH2, normFactor))
                return;
        }

        // SIMD AVX-512 CPU Fallback
        KdeKernels.VectorizedKde(samples, grid, bandwidth, outDensity);
    }

    #endregion

    #region Buffer Pooling & Execution Context

    internal sealed class GpuStatsStreamSlot : IDisposable
    {
        private static readonly ConcurrentQueue<GpuStatsStreamSlot> s_pool = new();
        private static int s_poolCount;
        private const int MaxPoolSize = 64;

        public IntPtr Stream { get; private set; }
        public IntPtr dSamples { get; private set; }
        public IntPtr dGrid { get; private set; }
        public IntPtr dOut { get; private set; }
        public nuint capSamples { get; private set; }
        public nuint capGrid { get; private set; }
        public nuint capOut { get; private set; }
        private bool _disposed;

        public GpuStatsStreamSlot()
        {
            int res = CuDriver.StreamCreate(out IntPtr stream, 0);
            if (res != 0)
            {
                throw new InvalidOperationException($"Failed to create CUDA stream: {res}");
            }
            Stream = stream;
        }

        public static GpuStatsStreamSlot Rent()
        {
            while (s_pool.TryDequeue(out var slot))
            {
                Interlocked.Decrement(ref s_poolCount);
                if (!slot._disposed) return slot;
            }
            return new GpuStatsStreamSlot();
        }

        public static void Return(GpuStatsStreamSlot? slot)
        {
            if (slot == null || slot._disposed) return;
            if (Interlocked.Increment(ref s_poolCount) <= MaxPoolSize)
            {
                s_pool.Enqueue(slot);
            }
            else
            {
                Interlocked.Decrement(ref s_poolCount);
                slot.Dispose();
            }
        }

        public void EnsureCapacity(nuint reqSamples, nuint reqGrid, nuint reqOut)
        {
            if (reqSamples > capSamples)
            {
                if (dSamples != IntPtr.Zero) CuDriver.MemFree(dSamples);
                CuDriver.MemAlloc(out IntPtr ptr, reqSamples);
                dSamples = ptr;
                capSamples = reqSamples;
            }
            if (reqGrid > capGrid)
            {
                if (dGrid != IntPtr.Zero) CuDriver.MemFree(dGrid);
                CuDriver.MemAlloc(out IntPtr ptr, reqGrid);
                dGrid = ptr;
                capGrid = reqGrid;
            }
            if (reqOut > capOut)
            {
                if (dOut != IntPtr.Zero) CuDriver.MemFree(dOut);
                CuDriver.MemAlloc(out IntPtr ptr, reqOut);
                dOut = ptr;
                capOut = reqOut;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (dSamples != IntPtr.Zero) { CuDriver.MemFree(dSamples); dSamples = IntPtr.Zero; }
            if (dGrid != IntPtr.Zero) { CuDriver.MemFree(dGrid); dGrid = IntPtr.Zero; }
            if (dOut != IntPtr.Zero) { CuDriver.MemFree(dOut); dOut = IntPtr.Zero; }
            if (Stream != IntPtr.Zero) { CuDriver.StreamDestroy(Stream); Stream = IntPtr.Zero; }
        }
    }

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
        try
        {
            nuint bytesSamples = (nuint)(numSamples * sizeof(float));
            nuint bytesGrid = (nuint)(numGrid * sizeof(float));
            nuint bytesOut = (nuint)(numGrid * sizeof(float));

            CuDriver.CtxSetCurrent(s_cuContext);
            var slot = GpuStatsStreamSlot.Rent();
            try
            {
                slot.EnsureCapacity(bytesSamples, bytesGrid, bytesOut);

                fixed (float* pSamples = samples, pGrid = grid, pOut = outDensity)
                {
                    CuDriver.MemcpyHtoDAsync(slot.dSamples, (IntPtr)pSamples, bytesSamples, slot.Stream);
                    CuDriver.MemcpyHtoDAsync(slot.dGrid, (IntPtr)pGrid, bytesGrid, slot.Stream);

                    IntPtr dSamples = slot.dSamples;
                    IntPtr dGrid = slot.dGrid;
                    IntPtr dOut = slot.dOut;

                    void** pArgs = stackalloc void*[7];
                    pArgs[0] = &dSamples;
                    pArgs[1] = &dGrid;
                    pArgs[2] = &dOut;
                    pArgs[3] = &numSamples;
                    pArgs[4] = &numGrid;
                    pArgs[5] = &invH2;
                    pArgs[6] = &normFactor;

                    uint blockSize = 256;
                    uint gridSize = (uint)((numGrid + blockSize - 1) / blockSize);

                    int launchRes = CuDriver.LaunchKernel(
                        s_fnKde1D,
                        gridSize, 1, 1,
                        blockSize, 1, 1,
                        0, slot.Stream,
                        (IntPtr)pArgs,
                        IntPtr.Zero);

                    if (launchRes == 0)
                    {
                        CuDriver.MemcpyDtoHAsync((IntPtr)pOut, slot.dOut, bytesOut, slot.Stream);
                        CuDriver.StreamSynchronize(slot.Stream);
                        return true;
                    }
                }
            }
            finally
            {
                GpuStatsStreamSlot.Return(slot);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    #endregion
}
