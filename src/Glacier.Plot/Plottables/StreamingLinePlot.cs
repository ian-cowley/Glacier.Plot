namespace Glacier.Plot.Plottables;

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;

/// <summary>
/// Thread-safe SPSC unmanaged circular ring buffer line plot engineered for zero-allocation 60/120 FPS real-time streaming feeds.
/// Uses 128-byte cache-line <see cref="PaddedCursor"/> isolation and sequence-stamped lock-free snapshots.
/// </summary>
public sealed unsafe class StreamingLinePlot : IPlottable, IDisposable
{
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedProducerState
    {
        [FieldOffset(0)]
        public long WriteSeq; // Even = quiescent, Odd = producer writing

        [FieldOffset(8)]
        public int Head;

        [FieldOffset(12)]
        public int Count;

        [FieldOffset(16)]
        public long TotalPushed;
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedCursor
    {
        [FieldOffset(0)]
        public long Value;

        [FieldOffset(8)]
        public long CachedRemote;
    }

    private float* _buffer;
    private readonly int _capacity;
    private PaddedProducerState _producer;
    private PaddedCursor _consumer;
    private readonly VectorPath _path = new();
    private int _disposed;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan, StrokeWidth = 2.0f };
    public DecimationStrategy Decimation { get; set; } = DecimationStrategy.Auto;

    public int Capacity => _capacity;
    public int Count => Volatile.Read(ref _producer.Count);
    public long TotalPushed => Volatile.Read(ref _producer.TotalPushed);

    public StreamingLinePlot(int capacity, PlotStyle? style = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _buffer = (float*)NativeMemory.AlignedAlloc(checked((nuint)capacity * (nuint)sizeof(float)), 64);
        _producer = default;
        _consumer = default;
        if (style != null) Style = style;
    }

    public void Append(float value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        long seq = _producer.WriteSeq;
        Volatile.Write(ref _producer.WriteSeq, seq + 1);
        Thread.MemoryBarrier();

        int head = _producer.Head;
        _buffer[head] = value;

        int nextHead = head + 1;
        if (nextHead >= _capacity) nextHead = 0;
        _producer.Head = nextHead;

        if (_producer.Count < _capacity)
            _producer.Count++;

        _producer.TotalPushed++;

        Volatile.Write(ref _producer.WriteSeq, seq + 2);
    }

    public void Append(ReadOnlySpan<float> values)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (values.IsEmpty) return;

        long seq = _producer.WriteSeq;
        Volatile.Write(ref _producer.WriteSeq, seq + 1);
        Thread.MemoryBarrier();

        if (values.Length >= _capacity)
        {
            values[^_capacity..].CopyTo(new Span<float>(_buffer, _capacity));
            _producer.Head = 0;
            _producer.Count = _capacity;
            _producer.TotalPushed += values.Length;
        }
        else
        {
            int head = _producer.Head;
            int chunk1 = Math.Min(values.Length, _capacity - head);
            values[..chunk1].CopyTo(new Span<float>(_buffer + head, chunk1));
            int chunk2 = values.Length - chunk1;
            if (chunk2 > 0)
            {
                values[chunk1..].CopyTo(new Span<float>(_buffer, chunk2));
            }

            _producer.Head = (head + values.Length) % _capacity;
            _producer.Count = Math.Min(_capacity, _producer.Count + values.Length);
            _producer.TotalPushed += values.Length;
        }

        Volatile.Write(ref _producer.WriteSeq, seq + 2);
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        long seq = _producer.WriteSeq;
        Volatile.Write(ref _producer.WriteSeq, seq + 1);
        Thread.MemoryBarrier();

        _producer.Head = 0;
        _producer.Count = 0;
        _producer.TotalPushed = 0;

        Volatile.Write(ref _producer.WriteSeq, seq + 2);
    }

    private void SnapshotInto(float[] dest, out int outCount, out long outTotalPushed)
    {
        var spin = new SpinWait();
        for (int attempt = 0; attempt < 32; attempt++)
        {
            long seq1 = Volatile.Read(ref _producer.WriteSeq);
            if ((seq1 & 1) != 0)
            {
                spin.SpinOnce();
                continue;
            }

            Thread.MemoryBarrier();

            int count = Volatile.Read(ref _producer.Count);
            int head = Volatile.Read(ref _producer.Head);
            long total = Volatile.Read(ref _producer.TotalPushed);

            if (count > 0)
            {
                int clampedCount = Math.Min(count, _capacity);
                int startIdx = (total >= _capacity) ? (head % _capacity) : 0;
                if ((uint)startIdx >= (uint)_capacity) startIdx = 0;

                int chunk1 = Math.Min(clampedCount, _capacity - startIdx);
                new ReadOnlySpan<float>(_buffer + startIdx, chunk1).CopyTo(dest.AsSpan(0, chunk1));
                int chunk2 = clampedCount - chunk1;
                if (chunk2 > 0)
                {
                    new ReadOnlySpan<float>(_buffer, chunk2).CopyTo(dest.AsSpan(chunk1, chunk2));
                }
                count = clampedCount;
            }

            Thread.MemoryBarrier();
            long seq2 = Volatile.Read(ref _producer.WriteSeq);
            if (seq1 == seq2)
            {
                Volatile.Write(ref _consumer.Value, seq2);
                Volatile.Write(ref _consumer.CachedRemote, seq2);
                outCount = count;
                outTotalPushed = total;
                return;
            }

            spin.SpinOnce();
        }

        // Bounded saturation fallback: producer is running an unthrottled tight Append loop;
        // take a bounds-clamped copy without blocking or throwing.
        int fbCount = Math.Clamp(Volatile.Read(ref _producer.Count), 0, _capacity);
        int fbHead = Volatile.Read(ref _producer.Head);
        if ((uint)fbHead >= (uint)_capacity) fbHead = 0;
        long fbTotal = Volatile.Read(ref _producer.TotalPushed);
        if (fbTotal < fbCount) fbTotal = fbCount;

        if (fbCount > 0)
        {
            int startIdx = (fbTotal >= _capacity) ? fbHead : 0;
            int chunk1 = Math.Min(fbCount, _capacity - startIdx);
            new ReadOnlySpan<float>(_buffer + startIdx, chunk1).CopyTo(dest.AsSpan(0, chunk1));
            int chunk2 = fbCount - chunk1;
            if (chunk2 > 0)
            {
                new ReadOnlySpan<float>(_buffer, chunk2).CopyTo(dest.AsSpan(chunk1, chunk2));
            }
        }

        long finalSeq = Volatile.Read(ref _producer.WriteSeq);
        Volatile.Write(ref _consumer.Value, finalSeq);
        Volatile.Write(ref _consumer.CachedRemote, finalSeq);
        outCount = fbCount;
        outTotalPushed = fbTotal;
    }

    public AxisLimits GetLimits()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _producer.Count) == 0) return AxisLimits.Default;

        float[] rented = ArrayPool<float>.Shared.Rent(_capacity);
        try
        {
            SnapshotInto(rented, out int count, out long totalPushed);
            if (count == 0) return AxisLimits.Default;

            float min = rented[0];
            float max = rented[0];

            for (int i = 1; i < count; i++)
            {
                float v = rented[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            if (max == min) { max += 1f; min -= 1f; }

            double startX = Math.Max(0, totalPushed - count);
            double endX = totalPushed > 0 ? totalPushed - 1 : 0;
            if (endX == startX) endX += 1.0;

            return new AxisLimits(startX, endX, min, max);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Volatile.Read(ref _producer.Count) < 2) return;

        float[] rentedLinear = ArrayPool<float>.Shared.Rent(_capacity);
        try
        {
            SnapshotInto(rentedLinear, out int count, out long totalPushed);
            if (count < 2) return;

            double startX = Math.Max(0, totalPushed - count);
            int targetPixels = Math.Max(100, (int)Math.Ceiling(converter.Dimensions.DataWidth));

            _path.Clear();

            if (Decimation != DecimationStrategy.None && count > targetPixels * 2)
            {
                LttbKernels.DownsampleUniformToPath(
                    rentedLinear.AsSpan(0, count),
                    (float)startX,
                    1.0f,
                    targetPixels,
                    converter,
                    _path);
            }
            else if (count >= 256)
            {
                float[] xData = ArrayPool<float>.Shared.Rent(count);
                float[] pxBuf = ArrayPool<float>.Shared.Rent(count);
                float[] pyBuf = ArrayPool<float>.Shared.Rent(count);
                try
                {
                    for (int i = 0; i < count; i++) xData[i] = (float)(startX + i);
                    converter.TransformCoordinates(
                        xData.AsSpan(0, count),
                        rentedLinear.AsSpan(0, count),
                        pxBuf.AsSpan(0, count),
                        pyBuf.AsSpan(0, count));

                    _path.MoveTo(pxBuf[0], pyBuf[0]);
                    for (int i = 1; i < count; i++)
                    {
                        _path.LineTo(pxBuf[i], pyBuf[i]);
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(xData);
                    ArrayPool<float>.Shared.Return(pxBuf);
                    ArrayPool<float>.Shared.Return(pyBuf);
                }
            }
            else
            {
                _path.MoveTo(converter.GetPixelX(startX), converter.GetPixelY(rentedLinear[0]));
                for (int i = 1; i < count; i++)
                {
                    _path.LineTo(converter.GetPixelX(startX + i), converter.GetPixelY(rentedLinear[i]));
                }
            }

            canvas.DrawPath(_path, new Paint(
                Style.Color,
                PaintStyle.Stroke,
                Style.StrokeWidth,
                StrokeJoin.Round,
                StrokeCap.Round));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rentedLinear);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (_buffer != null)
            {
                NativeMemory.AlignedFree(_buffer);
                _buffer = null;
            }
            _path.Clear();
            GC.SuppressFinalize(this);
        }
    }

    ~StreamingLinePlot() => Dispose();
}
