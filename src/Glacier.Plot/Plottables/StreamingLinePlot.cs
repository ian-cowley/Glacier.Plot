namespace Glacier.Plot.Plottables;

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using Glacier.Graphics;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;

/// <summary>
/// Unmanaged circular ring buffer line plot engineered for zero-allocation 60/120 FPS real-time streaming feeds.
/// </summary>
public sealed unsafe class StreamingLinePlot : IPlottable, IDisposable
{
    private float* _buffer;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private long _totalPushed;
    private bool _disposed;

    public string? Label { get; set; }
    public PlotStyle Style { get; set; } = new() { Color = Colors.Cyan, StrokeWidth = 2.0f };
    public DecimationStrategy Decimation { get; set; } = DecimationStrategy.Auto;

    public int Capacity => _capacity;
    public int Count => _count;
    public long TotalPushed => _totalPushed;

    public StreamingLinePlot(int capacity, PlotStyle? style = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
        _buffer = (float*)NativeMemory.AlignedAlloc((nuint)(capacity * sizeof(float)), 64);
        _head = 0;
        _count = 0;
        _totalPushed = 0;
        if (style != null) Style = style;
    }

    public void Append(float value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;
        _totalPushed++;
    }

    public void Append(ReadOnlySpan<float> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            Append(values[i]);
        }
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
        _totalPushed = 0;
    }

    public AxisLimits GetLimits()
    {
        if (_count == 0) return AxisLimits.Default;

        float min = _buffer[0];
        float max = _buffer[0];

        for (int i = 0; i < _count; i++)
        {
            float v = _buffer[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (max == min) { max += 1f; min -= 1f; }

        double startX = Math.Max(0, _totalPushed - _count);
        double endX = _totalPushed > 0 ? _totalPushed - 1 : 0;
        if (endX == startX) endX += 1.0;

        return new AxisLimits(startX, endX, min, max);
    }

    public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
    {
        if (_count < 2) return;

        // Unroll circular buffer into linear contiguous span using rented array
        float[] rentedLinear = ArrayPool<float>.Shared.Rent(_count);
        try
        {
            int startIdx = (_totalPushed >= _capacity) ? _head : 0;
            for (int i = 0; i < _count; i++)
            {
                int bufferIdx = (startIdx + i) % _capacity;
                rentedLinear[i] = _buffer[bufferIdx];
            }

            double startX = Math.Max(0, _totalPushed - _count);
            int targetPixels = Math.Max(100, (int)Math.Ceiling(converter.Dimensions.DataWidth));

            var path = new VectorPath();

            if (Decimation != DecimationStrategy.None && _count > targetPixels * 2)
            {
                LttbKernels.DownsampleUniformToPath(
                    rentedLinear.AsSpan(0, _count),
                    (float)startX,
                    1.0f,
                    targetPixels,
                    converter,
                    path);
            }
            else
            {
                path.MoveTo(converter.GetPixelX(startX), converter.GetPixelY(rentedLinear[0]));
                for (int i = 1; i < _count; i++)
                {
                    path.LineTo(converter.GetPixelX(startX + i), converter.GetPixelY(rentedLinear[i]));
                }
            }

            canvas.DrawPath(path, new Paint(
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
        if (!_disposed)
        {
            if (_buffer != null)
            {
                NativeMemory.AlignedFree(_buffer);
                _buffer = null;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    ~StreamingLinePlot() => Dispose();
}
