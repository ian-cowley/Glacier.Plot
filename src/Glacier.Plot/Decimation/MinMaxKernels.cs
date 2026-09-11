namespace Glacier.Plot.Decimation;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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
}
