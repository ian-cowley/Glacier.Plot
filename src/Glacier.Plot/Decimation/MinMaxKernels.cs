namespace Glacier.Plot.Decimation;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;

/// <summary>
/// High-speed Min-Max decimation kernel.
/// Preserves peak-to-peak signal envelope and extreme spikes with minimal computation.
/// Emits 2 points (min and max in chronological order) per pixel bucket.
/// </summary>
public static unsafe class MinMaxKernels
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Downsample(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY,
        Glacier.Plot.Core.GpuTarget target)
        => Glacier.Plot.Compute.GpuPlotAccelerator.MinMaxDownsample(xValues, yValues, targetPixelWidth, outX, outY, target);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Downsample(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY)
    {
        if (targetPixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetPixelWidth));
        if (xValues.Length != yValues.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        int totalPoints = xValues.Length;
        if (totalPoints == 0) return 0;

        int targetPoints = targetPixelWidth * 2;
        if (totalPoints <= targetPoints)
        {
            xValues.CopyTo(outX[..totalPoints]);
            yValues.CopyTo(outY[..totalPoints]);
            return totalPoints;
        }

        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outIndex = 0;

        fixed (float* pX = xValues)
        fixed (float* pY = yValues)
        fixed (float* pOutX = outX)
        fixed (float* pOutY = outY)
        {
            for (int bucket = 0; bucket < targetPixelWidth; bucket++)
            {
                int start = (int)(bucket * bucketSize);
                int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
                if (start >= end) continue;

                float minVal = pY[start];
                float maxVal = pY[start];
                int minIdx = start;
                int maxIdx = start;

                for (int i = start + 1; i < end; i++)
                {
                    float y = pY[i];
                    if (y < minVal)
                    {
                        minVal = y;
                        minIdx = i;
                    }
                    if (y > maxVal)
                    {
                        maxVal = y;
                        maxIdx = i;
                    }
                }

                // Preserve chronological order
                if (minIdx <= maxIdx)
                {
                    pOutX[outIndex] = pX[minIdx];
                    pOutY[outIndex++] = minVal;
                    if (minIdx != maxIdx)
                    {
                        pOutX[outIndex] = pX[maxIdx];
                        pOutY[outIndex++] = maxVal;
                    }
                }
                else
                {
                    pOutX[outIndex] = pX[maxIdx];
                    pOutY[outIndex++] = maxVal;
                    pOutX[outIndex] = pX[minIdx];
                    pOutY[outIndex++] = minVal;
                }
            }
        }

        return outIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int DownsampleUniform(
        ReadOnlySpan<float> yValues,
        float xStart,
        float xStep,
        int targetPixelWidth,
        Span<float> outX,
        Span<float> outY)
    {
        if (targetPixelWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetPixelWidth));

        int totalPoints = yValues.Length;
        if (totalPoints == 0) return 0;

        int targetPoints = targetPixelWidth * 2;
        if (totalPoints <= targetPoints)
        {
            for (int i = 0; i < totalPoints; i++)
            {
                outX[i] = xStart + i * xStep;
                outY[i] = yValues[i];
            }
            return totalPoints;
        }

        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outIndex = 0;

        fixed (float* pY = yValues)
        fixed (float* pOutX = outX)
        fixed (float* pOutY = outY)
        {
            for (int bucket = 0; bucket < targetPixelWidth; bucket++)
            {
                int start = (int)(bucket * bucketSize);
                int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
                if (start >= end) continue;

                float minVal = pY[start];
                float maxVal = pY[start];
                int minIdx = start;
                int maxIdx = start;

                for (int i = start + 1; i < end; i++)
                {
                    float y = pY[i];
                    if (y < minVal)
                    {
                        minVal = y;
                        minIdx = i;
                    }
                    if (y > maxVal)
                    {
                        maxVal = y;
                        maxIdx = i;
                    }
                }

                if (minIdx <= maxIdx)
                {
                    pOutX[outIndex] = xStart + minIdx * xStep;
                    pOutY[outIndex++] = minVal;
                    if (minIdx != maxIdx)
                    {
                        pOutX[outIndex] = xStart + maxIdx * xStep;
                        pOutY[outIndex++] = maxVal;
                    }
                }
                else
                {
                    pOutX[outIndex] = xStart + maxIdx * xStep;
                    pOutY[outIndex++] = maxVal;
                    pOutX[outIndex] = xStart + minIdx * xStep;
                    pOutY[outIndex++] = minVal;
                }
            }
        }

        return outIndex;
    }

    /// <summary>
    /// Decimates 2D time series points using Min-Max downsampling directly into a Glacier.Graphics VectorPath
    /// using screen coordinate transformation without allocating intermediate point objects.
    /// Each bucket is emitted as a disjoint vertical stroke (min→max within the pixel column)
    /// to avoid self-intersecting polygons when the path is stroked.
    /// </summary>
    public static int DownsampleToPath(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath path)
    {
        if (targetPixelWidth <= 0 || xValues.Length == 0) return 0;

        int totalPoints = xValues.Length;
        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outCount = 0;

        for (int bucket = 0; bucket < targetPixelWidth; bucket++)
        {
            int start = (int)(bucket * bucketSize);
            int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
            if (start >= end) continue;

            float minVal = yValues[start];
            float maxVal = yValues[start];
            float minX = xValues[start];
            float maxX = xValues[start];

            for (int i = start + 1; i < end; i++)
            {
                float y = yValues[i];
                if (y < minVal) { minVal = y; minX = xValues[i]; }
                if (y > maxVal) { maxVal = y; maxX = xValues[i]; }
            }

            float pxMin = converter.GetPixelX(minX);
            float pxMax = converter.GetPixelX(maxX);
            float pyMin = converter.GetPixelY(minVal);
            float pyMax = converter.GetPixelY(maxVal);

            // Emit as a vertical segment within this pixel column.
            // Use the average x pixel to keep it in the bucket's column.
            float bucketPx = (pxMin + pxMax) * 0.5f;
            path.MoveTo(bucketPx, pyMax);
            path.LineTo(bucketPx, pyMin);
            outCount += 2;
        }

        return outCount;
    }

    /// <summary>
    /// Decimates uniformly spaced 1D signal points using Min-Max downsampling directly into a Glacier.Graphics VectorPath
    /// using screen coordinate transformation without allocating intermediate point objects.
    /// Each bucket is emitted as a disjoint vertical stroke (max→min within the pixel column)
    /// to avoid self-intersecting polygons when the path is stroked.
    /// </summary>
    public static int DownsampleUniformToPath(
        ReadOnlySpan<float> yValues,
        float xStart,
        float xStep,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath path)
    {
        if (targetPixelWidth <= 0 || yValues.Length == 0) return 0;

        int totalPoints = yValues.Length;
        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outCount = 0;

        for (int bucket = 0; bucket < targetPixelWidth; bucket++)
        {
            int start = (int)(bucket * bucketSize);
            int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
            if (start >= end) continue;

            float minVal = yValues[start];
            float maxVal = yValues[start];
            int minIdx = start;
            int maxIdx = start;

            for (int i = start + 1; i < end; i++)
            {
                float y = yValues[i];
                if (y < minVal) { minVal = y; minIdx = i; }
                if (y > maxVal) { maxVal = y; maxIdx = i; }
            }

            // Use the bucket's center x pixel for the vertical segment.
            float bucketCenterX = xStart + ((minIdx + maxIdx) * 0.5f) * xStep;
            float bucketPx = converter.GetPixelX(bucketCenterX);
            float pyMin = converter.GetPixelY(minVal);
            float pyMax = converter.GetPixelY(maxVal);

            path.MoveTo(bucketPx, pyMax);
            path.LineTo(bucketPx, pyMin);
            outCount += 2;
        }

        return outCount;
    }
}

