namespace Glacier.Plot.Decimation;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
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
        GpuTarget target)
        => Compute.GpuPlotAccelerator.MinMaxDownsample(xValues, yValues, targetPixelWidth, outX, outY, target);

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

                FindBucketMinMax(pY, start, end, out float minVal, out float maxVal, out int minIdx, out int maxIdx);

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

                FindBucketMinMax(pY, start, end, out float minVal, out float maxVal, out int minIdx, out int maxIdx);

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
        => DownsampleToPathCore(xValues, yValues, targetPixelWidth, converter, path, fillEnvelopePath: null, baselineDataY: 0.0);

    internal static int DownsampleToPath(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath strokePath,
        VectorPath? fillEnvelopePath,
        double baselineDataY)
        => DownsampleToPathCore(xValues, yValues, targetPixelWidth, converter, strokePath, fillEnvelopePath, baselineDataY);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int DownsampleToPathCore(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath strokePath,
        VectorPath? fillEnvelopePath,
        double baselineDataY)
    {
        if (targetPixelWidth <= 0 || xValues.Length == 0 || yValues.Length == 0) return 0;

        int totalPoints = Math.Min(xValues.Length, yValues.Length);
        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outCount = 0;
        bool fillStarted = false;

        fixed (float* pX = xValues)
        fixed (float* pY = yValues)
        {
            for (int bucket = 0; bucket < targetPixelWidth; bucket++)
            {
                int start = (int)(bucket * bucketSize);
                int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
                if (start >= end) continue;

                FindBucketMinMax(pY, start, end, out float minVal, out float maxVal, out int minIdx, out int maxIdx);

                float pxMin = converter.GetPixelX(pX[minIdx]);
                float pxMax = converter.GetPixelX(pX[maxIdx]);
                float pyMin = converter.GetPixelY(minVal);
                float pyMax = converter.GetPixelY(maxVal);

                // Emit as a vertical segment within this pixel column.
                float bucketPx = (pxMin + pxMax) * 0.5f;
                strokePath.MoveTo(bucketPx, pyMax);
                strokePath.LineTo(bucketPx, pyMin);
                outCount += 2;

                if (fillEnvelopePath != null)
                {
                    float envPy = maxVal >= baselineDataY ? pyMax : pyMin;
                    if (!fillStarted)
                    {
                        fillEnvelopePath.MoveTo(bucketPx, envPy);
                        fillStarted = true;
                    }
                    else
                    {
                        fillEnvelopePath.LineTo(bucketPx, envPy);
                    }
                }
            }
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
        => DownsampleUniformToPathCore(yValues, xStart, xStep, targetPixelWidth, converter, path, fillEnvelopePath: null, baselineDataY: 0.0);

    internal static int DownsampleUniformToPath(
        ReadOnlySpan<float> yValues,
        float xStart,
        float xStep,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath strokePath,
        VectorPath? fillEnvelopePath,
        double baselineDataY)
        => DownsampleUniformToPathCore(yValues, xStart, xStep, targetPixelWidth, converter, strokePath, fillEnvelopePath, baselineDataY);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int DownsampleUniformToPathCore(
        ReadOnlySpan<float> yValues,
        float xStart,
        float xStep,
        int targetPixelWidth,
        in CoordinateConverter converter,
        VectorPath strokePath,
        VectorPath? fillEnvelopePath,
        double baselineDataY)
    {
        if (targetPixelWidth <= 0 || yValues.Length == 0) return 0;

        int totalPoints = yValues.Length;
        float bucketSize = (float)totalPoints / targetPixelWidth;
        int outCount = 0;
        bool fillStarted = false;

        fixed (float* pY = yValues)
        {
            for (int bucket = 0; bucket < targetPixelWidth; bucket++)
            {
                int start = (int)(bucket * bucketSize);
                int end = Math.Min((int)((bucket + 1) * bucketSize), totalPoints);
                if (start >= end) continue;

                FindBucketMinMax(pY, start, end, out float minVal, out float maxVal, out int minIdx, out int maxIdx);

                float bucketCenterX = xStart + ((minIdx + maxIdx) * 0.5f) * xStep;
                float bucketPx = converter.GetPixelX(bucketCenterX);
                float pyMin = converter.GetPixelY(minVal);
                float pyMax = converter.GetPixelY(maxVal);

                strokePath.MoveTo(bucketPx, pyMax);
                strokePath.LineTo(bucketPx, pyMin);
                outCount += 2;

                if (fillEnvelopePath != null)
                {
                    float envPy = maxVal >= baselineDataY ? pyMax : pyMin;
                    if (!fillStarted)
                    {
                        fillEnvelopePath.MoveTo(bucketPx, envPy);
                        fillStarted = true;
                    }
                    else
                    {
                        fillEnvelopePath.LineTo(bucketPx, envPy);
                    }
                }
            }
        }

        return outCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void FindBucketMinMax(
        float* pY,
        int start,
        int end,
        out float minVal,
        out float maxVal,
        out int minIdx,
        out int maxIdx)
    {
        float first = pY[start];
        if (float.IsNaN(first))
        {
            minVal = first;
            maxVal = first;
            minIdx = start;
            maxIdx = start;
            return;
        }

        int bucketLen = end - start;
        int i = start;

        if (Vector512.IsHardwareAccelerated && bucketLen >= 32)
        {
            var vFirst = Vector512.Create(first);
            var vStartIdx = Vector512.Create(start);
            var v0 = Vector512.Load(pY + start);
            var vValid0 = Vector512.Equals(v0, v0);
            var vMin = Vector512.ConditionalSelect(vValid0, v0, vFirst);
            var vMax = vMin;
            var vInitIdx = vStartIdx + Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
            var vMinIdx = Vector512.ConditionalSelect(vValid0.AsInt32(), vInitIdx, vStartIdx);
            var vMaxIdx = vMinIdx;
            var vStep = Vector512.Create(16);
            var vCurIdx = vInitIdx + vStep;

            i = start + 16;
            int limit = end - 16;
            for (; i <= limit; i += 16)
            {
                var v = Vector512.Load(pY + i);
                var lt = Vector512.LessThan(v, vMin);
                vMin = Vector512.ConditionalSelect(lt, v, vMin);
                vMinIdx = Vector512.ConditionalSelect(lt.AsInt32(), vCurIdx, vMinIdx);

                var gt = Vector512.GreaterThan(v, vMax);
                vMax = Vector512.ConditionalSelect(gt, v, vMax);
                vMaxIdx = Vector512.ConditionalSelect(gt.AsInt32(), vCurIdx, vMaxIdx);

                vCurIdx += vStep;
            }

            float* pMinVals = stackalloc float[16];
            float* pMaxVals = stackalloc float[16];
            int* pMinIndices = stackalloc int[16];
            int* pMaxIndices = stackalloc int[16];
            vMin.Store(pMinVals);
            vMax.Store(pMaxVals);
            vMinIdx.Store(pMinIndices);
            vMaxIdx.Store(pMaxIndices);

            minVal = pMinVals[0];
            minIdx = pMinIndices[0];
            maxVal = pMaxVals[0];
            maxIdx = pMaxIndices[0];

            for (int lane = 1; lane < 16; lane++)
            {
                float cMin = pMinVals[lane];
                int cMinIdx = pMinIndices[lane];
                if (cMin < minVal || (cMin == minVal && cMinIdx < minIdx))
                {
                    minVal = cMin;
                    minIdx = cMinIdx;
                }

                float cMax = pMaxVals[lane];
                int cMaxIdx = pMaxIndices[lane];
                if (cMax > maxVal || (cMax == maxVal && cMaxIdx < maxIdx))
                {
                    maxVal = cMax;
                    maxIdx = cMaxIdx;
                }
            }
        }
        else if (Vector256.IsHardwareAccelerated && bucketLen >= 16)
        {
            var vFirst = Vector256.Create(first);
            var vStartIdx = Vector256.Create(start);
            var v0 = Vector256.Load(pY + start);
            var vValid0 = Vector256.Equals(v0, v0);
            var vMin = Vector256.ConditionalSelect(vValid0, v0, vFirst);
            var vMax = vMin;
            var vInitIdx = vStartIdx + Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
            var vMinIdx = Vector256.ConditionalSelect(vValid0.AsInt32(), vInitIdx, vStartIdx);
            var vMaxIdx = vMinIdx;
            var vStep = Vector256.Create(8);
            var vCurIdx = vInitIdx + vStep;

            i = start + 8;
            int limit = end - 8;
            for (; i <= limit; i += 8)
            {
                var v = Vector256.Load(pY + i);
                var lt = Vector256.LessThan(v, vMin);
                vMin = Vector256.ConditionalSelect(lt, v, vMin);
                vMinIdx = Vector256.ConditionalSelect(lt.AsInt32(), vCurIdx, vMinIdx);

                var gt = Vector256.GreaterThan(v, vMax);
                vMax = Vector256.ConditionalSelect(gt, v, vMax);
                vMaxIdx = Vector256.ConditionalSelect(gt.AsInt32(), vCurIdx, vMaxIdx);

                vCurIdx += vStep;
            }

            float* pMinVals = stackalloc float[8];
            float* pMaxVals = stackalloc float[8];
            int* pMinIndices = stackalloc int[8];
            int* pMaxIndices = stackalloc int[8];
            vMin.Store(pMinVals);
            vMax.Store(pMaxVals);
            vMinIdx.Store(pMinIndices);
            vMaxIdx.Store(pMaxIndices);

            minVal = pMinVals[0];
            minIdx = pMinIndices[0];
            maxVal = pMaxVals[0];
            maxIdx = pMaxIndices[0];

            for (int lane = 1; lane < 8; lane++)
            {
                float cMin = pMinVals[lane];
                int cMinIdx = pMinIndices[lane];
                if (cMin < minVal || (cMin == minVal && cMinIdx < minIdx))
                {
                    minVal = cMin;
                    minIdx = cMinIdx;
                }

                float cMax = pMaxVals[lane];
                int cMaxIdx = pMaxIndices[lane];
                if (cMax > maxVal || (cMax == maxVal && cMaxIdx < maxIdx))
                {
                    maxVal = cMax;
                    maxIdx = cMaxIdx;
                }
            }
        }
        else
        {
            minVal = pY[start];
            maxVal = pY[start];
            minIdx = start;
            maxIdx = start;
            i = start + 1;
        }

        for (; i < end; i++)
        {
            float y = pY[i];
            if (y < minVal) { minVal = y; minIdx = i; }
            if (y > maxVal) { maxVal = y; maxIdx = i; }
        }
    }
}
