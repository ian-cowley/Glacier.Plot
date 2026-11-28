using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Glacier.Gpu.Compilation;
using Glacier.Gpu.Drivers;
using Glacier.Plot.Core;
using Glacier.Plot.Statistical.Compute;

namespace Glacier.Plot.Compute;

/// <summary>
/// Unified CUDA driver context, PTX module loader, and VRAM-capped stream/buffer pool
/// shared across <see cref="GpuPlotAccelerator"/> and <see cref="GpuStatsAccelerator"/>.
/// </summary>
internal static unsafe class CudaPlotContext
{
    private const int MaxPooledSlots = 8;
    private const nuint MaxIdleSlotVramBytes = (nuint)(256UL * 1024UL * 1024UL);
    private const nuint MaxTotalIdlePoolVramBytes = (nuint)(512UL * 1024UL * 1024UL);

    private static readonly Lock s_initLock = new();
    private static readonly object s_poolLock = new();
    private static readonly ReaderWriterLockSlim s_lifecycleLock = new(LockRecursionPolicy.NoRecursion);

    private static bool s_nvidiaInitialized;
    private static bool s_nvidiaAvailable;
    private static IntPtr s_cuContext;
    private static IntPtr s_plotModule;
    private static IntPtr s_statsModule;

    private static IntPtr s_fnMinMax;
    private static IntPtr s_fnTransformCoords;
    private static IntPtr s_fnBucketAvgs;
    private static IntPtr s_fnKde1D;
    private static IntPtr s_fnKde2D;

    private static readonly Stack<PlotCudaStreamSlot> s_freeSlots = new();
    private static int s_inUseSlots;
    private static nuint s_pooledIdleVramBytes;

    private static readonly AsyncLocal<bool> s_simulateNvidiaUnavailable = new();
    private static readonly AsyncLocal<bool> s_simulateExecutionFailure = new();

    static CudaPlotContext()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            try
            {
                ReleaseGpuResources();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Glacier.Plot ProcessExit CUDA cleanup warning: {ex.GetType().Name}: {ex.Message}");
            }
        };
    }

    internal static bool IsNvidiaAvailable
    {
        get
        {
            if (s_simulateNvidiaUnavailable.Value)
                return false;

            s_lifecycleLock.EnterReadLock();
            try
            {
                return !s_simulateNvidiaUnavailable.Value && EnsureNvidiaInitialized();
            }
            finally
            {
                s_lifecycleLock.ExitReadLock();
            }
        }
    }

    internal static IntPtr ContextHandle => s_cuContext;
    internal static IntPtr MinMaxFn => s_fnMinMax;
    internal static IntPtr TransformCoordsFn => s_fnTransformCoords;
    internal static IntPtr BucketAvgsFn => s_fnBucketAvgs;
    internal static IntPtr Kde1DFn => s_fnKde1D;
    internal static IntPtr Kde2DFn => s_fnKde2D;

    internal static int PooledSlotCount
    {
        get
        {
            lock (s_poolLock) return s_freeSlots.Count;
        }
    }

    internal static void ValidateTarget(GpuTarget target)
    {
        switch (target)
        {
            case GpuTarget.Auto:
            case GpuTarget.Cpu:
                return;
            case GpuTarget.Nvidia:
                if (!IsNvidiaAvailable)
                {
                    throw new PlatformNotSupportedException(
                        "NVIDIA CUDA backend is not available on this host; use GpuTarget.Auto or GpuTarget.Cpu.");
                }
                return;
            case GpuTarget.Amd:
                throw new PlatformNotSupportedException(
                    "AMD ROCm/HIP backend is not supported in Glacier.Plot; use GpuTarget.Auto, GpuTarget.Nvidia, or GpuTarget.Cpu.");
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unsupported GpuTarget value.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static nuint ComputeFloatBufferBytes(int elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        return checked((nuint)elementCount * (nuint)sizeof(float));
    }

    internal static bool CheckCuda(int status, string operation)
    {
        if (status == 0) return true;
        Trace.TraceWarning($"Glacier.Plot CUDA '{operation}' failed with status code {status}.");
        return false;
    }

    internal sealed class PlotCudaStreamSlot : IDisposable
    {
        public IntPtr Stream;
        public IntPtr D0;
        public IntPtr D1;
        public IntPtr D2;
        public IntPtr D3;
        public IntPtr D4;
        public nuint Cap0;
        public nuint Cap1;
        public nuint Cap2;
        public nuint Cap3;
        public nuint Cap4;
        private bool _disposed;

        public nuint TotalAllocatedBytes => Cap0 + Cap1 + Cap2 + Cap3 + Cap4;

        public PlotCudaStreamSlot(IntPtr stream)
        {
            Stream = stream;
        }

        public bool EnsureBuffers(nuint cap0, nuint cap1, nuint cap2, nuint cap3, nuint cap4 = 0)
        {
            return EnsureSingleBuffer(ref D0, ref Cap0, cap0)
                && EnsureSingleBuffer(ref D1, ref Cap1, cap1)
                && EnsureSingleBuffer(ref D2, ref Cap2, cap2)
                && EnsureSingleBuffer(ref D3, ref Cap3, cap3)
                && EnsureSingleBuffer(ref D4, ref Cap4, cap4);
        }

        private static bool EnsureSingleBuffer(ref IntPtr devicePtr, ref nuint capacity, nuint requiredBytes)
        {
            if (requiredBytes <= capacity)
                return true;

            if (devicePtr != IntPtr.Zero)
            {
                IntPtr oldPtr = devicePtr;
                devicePtr = IntPtr.Zero;
                capacity = 0;
                try
                {
                    int freeRes = CuDriver.MemFree(oldPtr);
                    if (freeRes != 0)
                        Trace.TraceWarning($"Glacier.Plot CUDA MemFree failed with status {freeRes}.");
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA MemFree exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            int allocRes = CuDriver.MemAlloc(out IntPtr ptr, requiredBytes);
            if (allocRes != 0 || ptr == IntPtr.Zero)
            {
                DrainIdleSlots();
                allocRes = CuDriver.MemAlloc(out ptr, requiredBytes);
                if (allocRes != 0 || ptr == IntPtr.Zero)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA MemAlloc({requiredBytes} bytes) failed with status {allocRes}.");
                    return false;
                }
            }

            devicePtr = ptr;
            capacity = requiredBytes;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Stream != IntPtr.Zero)
            {
                IntPtr stream = Stream;
                Stream = IntPtr.Zero;
                try
                {
                    int syncRes = CuDriver.StreamSynchronize(stream);
                    if (syncRes != 0)
                        Trace.TraceWarning($"Glacier.Plot CUDA StreamSynchronize on dispose failed with status {syncRes}.");
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA StreamSynchronize exception: {ex.GetType().Name}: {ex.Message}");
                }
                try
                {
                    int destroyRes = CuDriver.StreamDestroy(stream);
                    if (destroyRes != 0)
                        Trace.TraceWarning($"Glacier.Plot CUDA StreamDestroy failed with status {destroyRes}.");
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA StreamDestroy exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            FreeBuffer(ref D0, ref Cap0);
            FreeBuffer(ref D1, ref Cap1);
            FreeBuffer(ref D2, ref Cap2);
            FreeBuffer(ref D3, ref Cap3);
            FreeBuffer(ref D4, ref Cap4);
        }

        private static void FreeBuffer(ref IntPtr ptr, ref nuint cap)
        {
            if (ptr == IntPtr.Zero) return;
            IntPtr p = ptr;
            ptr = IntPtr.Zero;
            cap = 0;
            try
            {
                int res = CuDriver.MemFree(p);
                if (res != 0)
                    Trace.TraceWarning($"Glacier.Plot CUDA MemFree on dispose failed with status {res}.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Glacier.Plot CUDA MemFree exception: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    internal static bool TryEnterNvidiaExecutionReadLock()
    {
        if (s_simulateNvidiaUnavailable.Value)
            return false;

        s_lifecycleLock.EnterReadLock();
        bool transferLockToCaller = false;
        try
        {
            if (!s_simulateNvidiaUnavailable.Value &&
                EnsureNvidiaInitialized() &&
                Volatile.Read(ref s_nvidiaAvailable) &&
                s_cuContext != IntPtr.Zero)
            {
                transferLockToCaller = true;
                return true;
            }

            return false;
        }
        finally
        {
            if (!transferLockToCaller)
                s_lifecycleLock.ExitReadLock();
        }
    }

    internal static void ExitNvidiaExecutionReadLock()
    {
        s_lifecycleLock.ExitReadLock();
    }

    internal static PlotCudaStreamSlot? RentSlot()
    {
        if (s_simulateExecutionFailure.Value)
        {
            Trace.TraceWarning("Glacier.Plot CUDA simulated execution failure in RentSlot.");
            return null;
        }

        PlotCudaStreamSlot? slot = null;
        lock (s_poolLock)
        {
            if (s_freeSlots.Count > 0)
            {
                slot = s_freeSlots.Pop();
                s_pooledIdleVramBytes -= slot.TotalAllocatedBytes;
            }
            s_inUseSlots++;
        }

        if (slot != null)
            return slot;

        int streamRes = CuDriver.StreamCreate(out IntPtr stream, CuDriver.CU_STREAM_NON_BLOCKING);
        if (streamRes != 0 || stream == IntPtr.Zero)
        {
            Trace.TraceWarning($"Glacier.Plot CUDA StreamCreate failed with status {streamRes}.");
            lock (s_poolLock)
            {
                s_inUseSlots--;
                Monitor.PulseAll(s_poolLock);
            }
            return null;
        }

        return new PlotCudaStreamSlot(stream);
    }

    internal static void ReturnSlot(PlotCudaStreamSlot slot, bool poison = false)
    {
        bool shouldDispose = poison;
        nuint slotBytes = slot.TotalAllocatedBytes;

        lock (s_poolLock)
        {
            if (!shouldDispose &&
                (s_freeSlots.Count >= MaxPooledSlots ||
                 slotBytes > MaxIdleSlotVramBytes ||
                 s_pooledIdleVramBytes + slotBytes > MaxTotalIdlePoolVramBytes))
            {
                shouldDispose = true;
            }

            if (!shouldDispose)
            {
                s_freeSlots.Push(slot);
                s_pooledIdleVramBytes += slotBytes;
            }

            s_inUseSlots--;
            Monitor.PulseAll(s_poolLock);
        }

        if (shouldDispose)
        {
            slot.Dispose();
        }
    }

    private static void DrainIdleSlots()
    {
        List<PlotCudaStreamSlot>? toDispose = null;
        lock (s_poolLock)
        {
            if (s_freeSlots.Count > 0)
            {
                toDispose = new List<PlotCudaStreamSlot>(s_freeSlots.Count);
                while (s_freeSlots.Count > 0)
                    toDispose.Add(s_freeSlots.Pop());
                s_pooledIdleVramBytes = 0;
            }
        }

        if (toDispose != null)
        {
            foreach (var idle in toDispose)
                idle.Dispose();
        }
    }

    private static bool EnsureNvidiaInitialized()
    {
        if (Volatile.Read(ref s_nvidiaInitialized))
            return Volatile.Read(ref s_nvidiaAvailable);

        lock (s_initLock)
        {
            if (s_nvidiaInitialized)
                return s_nvidiaAvailable;

            try
            {
                if (!CuDriver.IsAvailable())
                {
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                int initRes = CuDriver.Init(0);
                if (initRes != 0 || CuDriver.DeviceGet(out int dev, 0) != 0)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA Init/DeviceGet failed (init={initRes}).");
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                if (CuDriver.DeviceGetAttribute(out int major, 75 /* CU_DEVICE_ATTRIBUTE_COMPUTE_CAPABILITY_MAJOR */, dev) != 0 ||
                    CuDriver.DeviceGetAttribute(out int minor, 76 /* CU_DEVICE_ATTRIBUTE_COMPUTE_CAPABILITY_MINOR */, dev) != 0)
                {
                    Trace.TraceWarning("Glacier.Plot CUDA DeviceGetAttribute failed for compute capability.");
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                string targetArch = $"sm_{major}{minor}";

                int ctxRes = CuDriver.CtxCreate(out s_cuContext, 0, dev);
                if (ctxRes != 0 || s_cuContext == IntPtr.Zero)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA CtxCreate failed with status {ctxRes}.");
                    s_cuContext = IntPtr.Zero;
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                string plotPtx = KernelCompiler.NormalizePtxDirectives(GpuPlotKernels.PtxSource, targetArch);
                byte[] plotPtxBytes = Encoding.UTF8.GetBytes(plotPtx + "\0");
                int plotModRes = CuDriver.ModuleLoadData(out s_plotModule, plotPtxBytes);
                if (plotModRes != 0 || s_plotModule == IntPtr.Zero)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA ModuleLoadData(plot) failed with status {plotModRes}.");
                    s_plotModule = IntPtr.Zero;
                    RollbackPartialInit();
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                string statsPtx = KernelCompiler.NormalizePtxDirectives(GpuStatsKernels.PtxSource, targetArch);
                byte[] statsPtxBytes = Encoding.UTF8.GetBytes(statsPtx + "\0");
                int statsModRes = CuDriver.ModuleLoadData(out s_statsModule, statsPtxBytes);
                if (statsModRes != 0 || s_statsModule == IntPtr.Zero)
                {
                    Trace.TraceWarning($"Glacier.Plot CUDA ModuleLoadData(stats) failed with status {statsModRes}.");
                    s_statsModule = IntPtr.Zero;
                    RollbackPartialInit();
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                if (CuDriver.ModuleGetFunction(out s_fnMinMax, s_plotModule, "plot_minmax_decimate_fp32") != 0 ||
                    CuDriver.ModuleGetFunction(out s_fnTransformCoords, s_plotModule, "plot_transform_coords_fp32") != 0 ||
                    CuDriver.ModuleGetFunction(out s_fnBucketAvgs, s_plotModule, "plot_lttb_bucket_averages_fp32") != 0 ||
                    CuDriver.ModuleGetFunction(out s_fnKde1D, s_statsModule, "statsviz_kde_evaluate_fp32") != 0 ||
                    CuDriver.ModuleGetFunction(out s_fnKde2D, s_statsModule, "statsviz_kde2d_evaluate_fp32") != 0 ||
                    s_fnMinMax == IntPtr.Zero ||
                    s_fnTransformCoords == IntPtr.Zero ||
                    s_fnBucketAvgs == IntPtr.Zero ||
                    s_fnKde1D == IntPtr.Zero ||
                    s_fnKde2D == IntPtr.Zero)
                {
                    Trace.TraceWarning("Glacier.Plot CUDA ModuleGetFunction failed for one or more embedded PTX kernels.");
                    RollbackPartialInit();
                    Volatile.Write(ref s_nvidiaAvailable, false);
                    return false;
                }

                Volatile.Write(ref s_nvidiaAvailable, true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Trace.TraceWarning($"Glacier.Plot CUDA initialization failed: {ex.GetType().Name}: {ex.Message}");
                RollbackPartialInit();
                Volatile.Write(ref s_nvidiaAvailable, false);
            }
            finally
            {
                Volatile.Write(ref s_nvidiaInitialized, true);
            }

            return Volatile.Read(ref s_nvidiaAvailable);
        }
    }

    private static void RollbackPartialInit()
    {
        s_fnMinMax = IntPtr.Zero;
        s_fnTransformCoords = IntPtr.Zero;
        s_fnBucketAvgs = IntPtr.Zero;
        s_fnKde1D = IntPtr.Zero;
        s_fnKde2D = IntPtr.Zero;

        if (s_plotModule != IntPtr.Zero)
        {
            IntPtr mod = s_plotModule;
            s_plotModule = IntPtr.Zero;
            try
            {
                int res = CuDriver.ModuleUnload(mod);
                if (res != 0) Trace.TraceWarning($"Glacier.Plot CUDA ModuleUnload(plot) failed with status {res}.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Glacier.Plot CUDA ModuleUnload(plot) exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (s_statsModule != IntPtr.Zero)
        {
            IntPtr mod = s_statsModule;
            s_statsModule = IntPtr.Zero;
            try
            {
                int res = CuDriver.ModuleUnload(mod);
                if (res != 0) Trace.TraceWarning($"Glacier.Plot CUDA ModuleUnload(stats) failed with status {res}.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Glacier.Plot CUDA ModuleUnload(stats) exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (s_cuContext != IntPtr.Zero)
        {
            IntPtr ctx = s_cuContext;
            s_cuContext = IntPtr.Zero;
            try
            {
                int res = CuDriver.CtxDestroy(ctx);
                if (res != 0) Trace.TraceWarning($"Glacier.Plot CUDA CtxDestroy failed with status {res}.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Glacier.Plot CUDA CtxDestroy exception: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Releases all pooled CUDA streams, device buffers, loaded PTX modules, and the shared CUDA context.
    /// Safe to call multiple times; subsequent GPU calls will re-initialize CUDA cleanly.
    /// </summary>
    internal static void ReleaseGpuResources()
    {
        s_lifecycleLock.EnterWriteLock();
        try
        {
            lock (s_initLock)
            {
                List<PlotCudaStreamSlot> toDispose;
                lock (s_poolLock)
                {
                    while (s_inUseSlots > 0)
                        Monitor.Wait(s_poolLock, 50);

                    toDispose = new List<PlotCudaStreamSlot>(s_freeSlots.Count);
                    while (s_freeSlots.Count > 0)
                        toDispose.Add(s_freeSlots.Pop());
                    s_pooledIdleVramBytes = 0;
                }

                if (s_cuContext != IntPtr.Zero)
                {
                    try
                    {
                        int res = CuDriver.CtxSetCurrent(s_cuContext);
                        if (res != 0)
                            Trace.TraceWarning($"Glacier.Plot CUDA CtxSetCurrent during release failed with status {res}.");
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"Glacier.Plot CUDA CtxSetCurrent exception: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                foreach (var slot in toDispose)
                {
                    slot.Dispose();
                }

                RollbackPartialInit();
                Volatile.Write(ref s_nvidiaAvailable, false);
                Volatile.Write(ref s_nvidiaInitialized, false);
            }
        }
        finally
        {
            s_lifecycleLock.ExitWriteLock();
        }
    }

    internal static IDisposable BeginTestNvidiaUnavailableScope()
        => new TestSimulationScope(simulateNvidiaUnavailable: true, simulateExecutionFailure: null);

    internal static IDisposable BeginTestExecutionFailureScope()
        => new TestSimulationScope(simulateNvidiaUnavailable: null, simulateExecutionFailure: true);

    private sealed class TestSimulationScope : IDisposable
    {
        private readonly bool _prevUnavailable;
        private readonly bool _prevExecutionFailure;
        private bool _disposed;

        public TestSimulationScope(bool? simulateNvidiaUnavailable, bool? simulateExecutionFailure)
        {
            _prevUnavailable = s_simulateNvidiaUnavailable.Value;
            _prevExecutionFailure = s_simulateExecutionFailure.Value;
            if (simulateNvidiaUnavailable.HasValue)
                s_simulateNvidiaUnavailable.Value = simulateNvidiaUnavailable.Value;
            if (simulateExecutionFailure.HasValue)
                s_simulateExecutionFailure.Value = simulateExecutionFailure.Value;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            s_simulateNvidiaUnavailable.Value = _prevUnavailable;
            s_simulateExecutionFailure.Value = _prevExecutionFailure;
        }
    }
}
