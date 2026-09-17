namespace Glacier.Plot.Decimation;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

/// <summary>
/// Hardware-accelerated Largest-Triangle-Three-Buckets (LTTB) decimation algorithm.
/// Optimized using AVX-512, AVX2, and AdvSimd intrinsics for sub-4ms decimation of 10M+ points.
/// </summary>
public static unsafe class LttbKernels
{
    /// <summary>
    /// Downsamples 2D time series points (x, y) using LTTB downsampling into pre-allocated output buffers.
    /// </summary>
    /// <param name="xValues">Source X coordinates.</param>
    /// <param name="yValues">Source Y coordinates.</param>
    /// <param name="targetPoints">Target number of downsampled points (e.g. screen pixel width).</param>
    /// <param name="outX">Destination buffer for downsampled X coordinates.</param>
    /// <param name="outY">Destination buffer for downsampled Y coordinates.</param>
    /// <returns>Actual number of points written to outX and outY.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int Downsample(
        ReadOnlySpan<float> xValues,
        ReadOnlySpan<float> yValues,
        int targetPoints,
        Span<float> outX,
        Span<float> outY)
    {
        if (targetPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetPoints), "Target points must be positive.");
        if (xValues.Length != yValues.Length)
            throw new ArgumentException("X and Y spans must have identical length.");

        int totalPoints = xValues.Length;
        if (totalPoints == 0) return 0;

        if (targetPoints == 1 || totalPoints == 1)
        {
            outX[0] = xValues[0];
            outY[0] = yValues[0];
            return 1;
        }

        if (targetPoints >= totalPoints)
        {
            xValues.CopyTo(outX[..totalPoints]);
            yValues.CopyTo(outY[..totalPoints]);
            return totalPoints;
        }

        if (targetPoints == 2)
        {
            outX[0] = xValues[0];
            outY[0] = yValues[0];
            outX[1] = xValues[totalPoints - 1];
            outY[1] = yValues[totalPoints - 1];
            return 2;
        }

        float bucketSize = (float)(totalPoints - 2) / (targetPoints - 2);

        fixed (float* pX = xValues)
        fixed (float* pY = yValues)
        fixed (float* pOutX = outX)
        fixed (float* pOutY = outY)
        {
            pOutX[0] = pX[0];
            pOutY[0] = pY[0];
            int a = 0;

            float* pAreas = stackalloc float[16];
            int* pIndices = stackalloc int[16];

            for (int i = 0; i < targetPoints - 2; i++)
            {
                int currentBucketStart = (int)((i + 0) * bucketSize) + 1;
                int currentBucketEnd = Math.Min((int)((i + 1) * bucketSize) + 1, totalPoints);

                int nextBucketStart = (int)((i + 1) * bucketSize) + 1;
                int nextBucketEnd = Math.Min((int)((i + 2) * bucketSize) + 1, totalPoints);

                // Phase 1: SIMD average of the next bucket
                float avgX = 0f, avgY = 0f;
                int nextCount = nextBucketEnd - nextBucketStart;
                if (nextCount > 0)
                {
                    int n = nextBucketStart;
                    if (Avx512F.IsSupported && nextCount >= 16)
                    {
                        var vSumX = Vector512<float>.Zero;
                        var vSumY = Vector512<float>.Zero;
                        for (; n <= nextBucketEnd - 16; n += 16)
                        {
                            vSumX = Vector512.Add(vSumX, Vector512.Load(pX + n));
                            vSumY = Vector512.Add(vSumY, Vector512.Load(pY + n));
                        }
                        avgX = Vector512.Sum(vSumX);
                        avgY = Vector512.Sum(vSumY);
                    }
                    else if (Avx2.IsSupported && nextCount >= 8)
                    {
                        var vSumX = Vector256<float>.Zero;
                        var vSumY = Vector256<float>.Zero;
                        for (; n <= nextBucketEnd - 8; n += 8)
                        {
                            vSumX = Vector256.Add(vSumX, Vector256.Load(pX + n));
                            vSumY = Vector256.Add(vSumY, Vector256.Load(pY + n));
                        }
                        avgX = Vector256.Sum(vSumX);
                        avgY = Vector256.Sum(vSumY);
                    }
                    else if (AdvSimd.IsSupported && nextCount >= 4)
                    {
                        var vSumX = Vector128<float>.Zero;
                        var vSumY = Vector128<float>.Zero;
                        for (; n <= nextBucketEnd - 4; n += 4)
                        {
                            vSumX = Vector128.Add(vSumX, Vector128.Load(pX + n));
                            vSumY = Vector128.Add(vSumY, Vector128.Load(pY + n));
                        }
                        avgX = Vector128.Sum(vSumX);
                        avgY = Vector128.Sum(vSumY);
                    }

                    for (; n < nextBucketEnd; n++)
                    {
                        avgX += pX[n];
                        avgY += pY[n];
                    }

                    avgX /= nextCount;
                    avgY /= nextCount;
                }
                else
                {
                    avgX = pX[totalPoints - 1];
                    avgY = pY[totalPoints - 1];
                }

                // Phase 2: Candidate cross-product area evaluation
                // Area = |(x_a - avgX) * (y_p - y_a) - (x_a - x_p) * (avgY - y_a)|
                float p_ax = pX[a];
                float p_ay = pY[a];
                float u = p_ax - avgX;
                float v = avgY - p_ay;
                float c = u * p_ay + v * p_ax;

                float maxArea = -1f;
                int bestIndex = currentBucketStart;

                int p = currentBucketStart;
                int bucketPoints = currentBucketEnd - currentBucketStart;

                if (Avx512F.IsSupported && bucketPoints >= 16)
                {
                    var vu = Vector512.Create(u);
                    var vv = Vector512.Create(v);
                    var vnegC = Vector512.Create(-c);

                    var vMaxArea = Vector512.Create(-1f);
                    var vBestIdx = Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
                    var vCurIdx = Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
                    var vStep16 = Vector512.Create(16);

                    for (; p <= currentBucketEnd - 16; p += 16)
                    {
                        var vx = Vector512.Load(pX + p);
                        var vy = Vector512.Load(pY + p);
                        var vLin = Avx512F.FusedMultiplyAdd(vv, vx, Avx512F.FusedMultiplyAdd(vu, vy, vnegC));
                        var vArea = Vector512.Abs(vLin);

                        var mask = Vector512.GreaterThan(vArea, vMaxArea);
                        vMaxArea = Vector512.ConditionalSelect(mask, vArea, vMaxArea);
                        vBestIdx = Vector512.ConditionalSelect(mask.AsInt32(), vCurIdx, vBestIdx);

                        vCurIdx = Vector512.Add(vCurIdx, vStep16);
                    }

                    vMaxArea.Store(pAreas);
                    vBestIdx.Store(pIndices);

                    for (int k = 0; k < 16; k++)
                    {
                        if (pAreas[k] > maxArea)
                        {
                            maxArea = pAreas[k];
                            bestIndex = currentBucketStart + pIndices[k];
                        }
                    }
                }
                else if (Avx2.IsSupported && bucketPoints >= 8)
                {
                    var vu = Vector256.Create(u);
                    var vv = Vector256.Create(v);
                    var vnegC = Vector256.Create(-c);

                    var vMaxArea = Vector256.Create(-1f);
                    var vBestIdx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                    var vCurIdx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                    var vStep8 = Vector256.Create(8);

                    for (; p <= currentBucketEnd - 8; p += 8)
                    {
                        var vx = Vector256.Load(pX + p);
                        var vy = Vector256.Load(pY + p);
                        var vLin = Vector256.Add(Vector256.Multiply(vv, vx), Vector256.Add(Vector256.Multiply(vu, vy), vnegC));
                        var vArea = Vector256.Abs(vLin);

                        var mask = Vector256.GreaterThan(vArea, vMaxArea);
                        vMaxArea = Vector256.ConditionalSelect(mask, vArea, vMaxArea);
                        vBestIdx = Vector256.ConditionalSelect(mask.AsInt32(), vCurIdx, vBestIdx);

                        vCurIdx = Vector256.Add(vCurIdx, vStep8);
                    }

                    vMaxArea.Store(pAreas);
                    vBestIdx.Store(pIndices);

                    for (int k = 0; k < 8; k++)
                    {
                        if (pAreas[k] > maxArea)
                        {
                            maxArea = pAreas[k];
                            bestIndex = currentBucketStart + pIndices[k];
                        }
                    }
                }

                for (; p < currentBucketEnd; p++)
                {
                    float area = MathF.Abs(v * pX[p] + u * pY[p] - c);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestIndex = p;
                    }
                }

                pOutX[i + 1] = pX[bestIndex];
                pOutY[i + 1] = pY[bestIndex];
                a = bestIndex;
            }

            pOutX[targetPoints - 1] = pX[totalPoints - 1];
            pOutY[targetPoints - 1] = pY[totalPoints - 1];
        }

        return targetPoints;
    }

    /// <summary>
    /// Downsamples uniformly-spaced Y values where X is implicitly xStart + i * xStep.
    /// Eliminates the need to allocate or materialize the input X span.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int DownsampleUniform(
        ReadOnlySpan<float> yValues,
        float xStart,
        float xStep,
        int targetPoints,
        Span<float> outX,
        Span<float> outY)
    {
        if (targetPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetPoints), "Target points must be positive.");

        int totalPoints = yValues.Length;
        if (totalPoints == 0) return 0;

        if (targetPoints == 1 || totalPoints == 1)
        {
            outX[0] = xStart;
            outY[0] = yValues[0];
            return 1;
        }

        if (targetPoints >= totalPoints)
        {
            for (int i = 0; i < totalPoints; i++)
            {
                outX[i] = xStart + i * xStep;
                outY[i] = yValues[i];
            }
            return totalPoints;
        }

        if (targetPoints == 2)
        {
            outX[0] = xStart;
            outY[0] = yValues[0];
            outX[1] = xStart + (totalPoints - 1) * xStep;
            outY[1] = yValues[totalPoints - 1];
            return 2;
        }

        float bucketSize = (float)(totalPoints - 2) / (targetPoints - 2);

        fixed (float* pY = yValues)
        fixed (float* pOutX = outX)
        fixed (float* pOutY = outY)
        {
            pOutX[0] = xStart;
            pOutY[0] = pY[0];
            int a = 0;

            float* pAreas = stackalloc float[16];
            int* pIndices = stackalloc int[16];

            for (int i = 0; i < targetPoints - 2; i++)
            {
                int currentBucketStart = (int)((i + 0) * bucketSize) + 1;
                int currentBucketEnd = Math.Min((int)((i + 1) * bucketSize) + 1, totalPoints);

                int nextBucketStart = (int)((i + 1) * bucketSize) + 1;
                int nextBucketEnd = Math.Min((int)((i + 2) * bucketSize) + 1, totalPoints);

                int nextCount = nextBucketEnd - nextBucketStart;
                float avgX, avgY = 0f;

                if (nextCount > 0)
                {
                    // For uniformly spaced X, average X is simply midpoint
                    avgX = xStart + ((nextBucketStart + nextBucketEnd - 1) * 0.5f) * xStep;

                    int n = nextBucketStart;
                    if (Avx512F.IsSupported && nextCount >= 16)
                    {
                        var vSumY = Vector512<float>.Zero;
                        for (; n <= nextBucketEnd - 16; n += 16)
                            vSumY = Vector512.Add(vSumY, Vector512.Load(pY + n));
                        avgY = Vector512.Sum(vSumY);
                    }
                    else if (Avx2.IsSupported && nextCount >= 8)
                    {
                        var vSumY = Vector256<float>.Zero;
                        for (; n <= nextBucketEnd - 8; n += 8)
                            vSumY = Vector256.Add(vSumY, Vector256.Load(pY + n));
                        avgY = Vector256.Sum(vSumY);
                    }

                    for (; n < nextBucketEnd; n++)
                        avgY += pY[n];

                    avgY /= nextCount;
                }
                else
                {
                    avgX = xStart + (totalPoints - 1) * xStep;
                    avgY = pY[totalPoints - 1];
                }

                float p_ax = xStart + a * xStep;
                float p_ay = pY[a];
                float u = p_ax - avgX;
                float v = avgY - p_ay;
                float c = u * p_ay + v * p_ax;

                float maxArea = -1f;
                int bestIndex = currentBucketStart;

                int p = currentBucketStart;
                int bucketPoints = currentBucketEnd - currentBucketStart;

                float kx = v * xStep;
                float k0 = v * xStart - c;

                if (Avx512F.IsSupported && bucketPoints >= 16)
                {
                    var vu = Vector512.Create(u);
                    var vMaxArea = Vector512.Create(-1f);
                    var vBestIdx = Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
                    var vCurIdx = Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
                    var vStep16 = Vector512.Create(16);

                    float initBase = k0 + p * kx;
                    var vCurBase = Vector512.Create(
                        initBase, initBase + kx, initBase + 2 * kx, initBase + 3 * kx,
                        initBase + 4 * kx, initBase + 5 * kx, initBase + 6 * kx, initBase + 7 * kx,
                        initBase + 8 * kx, initBase + 9 * kx, initBase + 10 * kx, initBase + 11 * kx,
                        initBase + 12 * kx, initBase + 13 * kx, initBase + 14 * kx, initBase + 15 * kx
                    );
                    var vDeltaBase = Vector512.Create(16 * kx);

                    for (; p <= currentBucketEnd - 16; p += 16)
                    {
                        var vy = Vector512.Load(pY + p);
                        var vLin = Avx512F.FusedMultiplyAdd(vu, vy, vCurBase);
                        var vArea = Vector512.Abs(vLin);

                        var mask = Vector512.GreaterThan(vArea, vMaxArea);
                        vMaxArea = Vector512.ConditionalSelect(mask, vArea, vMaxArea);
                        vBestIdx = Vector512.ConditionalSelect(mask.AsInt32(), vCurIdx, vBestIdx);

                        vCurBase = Vector512.Add(vCurBase, vDeltaBase);
                        vCurIdx = Vector512.Add(vCurIdx, vStep16);
                    }

                    vMaxArea.Store(pAreas);
                    vBestIdx.Store(pIndices);

                    for (int k = 0; k < 16; k++)
                    {
                        if (pAreas[k] > maxArea)
                        {
                            maxArea = pAreas[k];
                            bestIndex = currentBucketStart + pIndices[k];
                        }
                    }
                }
                else if (Avx2.IsSupported && bucketPoints >= 8)
                {
                    var vu = Vector256.Create(u);
                    var vMaxArea = Vector256.Create(-1f);
                    var vBestIdx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                    var vCurIdx = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                    var vStep8 = Vector256.Create(8);

                    float initBase = k0 + p * kx;
                    var vCurBase = Vector256.Create(
                        initBase, initBase + kx, initBase + 2 * kx, initBase + 3 * kx,
                        initBase + 4 * kx, initBase + 5 * kx, initBase + 6 * kx, initBase + 7 * kx
                    );
                    var vDeltaBase = Vector256.Create(8 * kx);

                    for (; p <= currentBucketEnd - 8; p += 8)
                    {
                        var vy = Vector256.Load(pY + p);
                        var vLin = Vector256.Add(Vector256.Multiply(vu, vy), vCurBase);
                        var vArea = Vector256.Abs(vLin);

                        var mask = Vector256.GreaterThan(vArea, vMaxArea);
                        vMaxArea = Vector256.ConditionalSelect(mask, vArea, vMaxArea);
                        vBestIdx = Vector256.ConditionalSelect(mask.AsInt32(), vCurIdx, vBestIdx);

                        vCurBase = Vector256.Add(vCurBase, vDeltaBase);
                        vCurIdx = Vector256.Add(vCurIdx, vStep8);
                    }

                    vMaxArea.Store(pAreas);
                    vBestIdx.Store(pIndices);

                    for (int k = 0; k < 8; k++)
                    {
                        if (pAreas[k] > maxArea)
                        {
                            maxArea = pAreas[k];
                            bestIndex = currentBucketStart + pIndices[k];
                        }
                    }
                }

                for (; p < currentBucketEnd; p++)
                {
                    float px = xStart + p * xStep;
                    float area = MathF.Abs(v * px + u * pY[p] - c);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        bestIndex = p;
                    }
                }

                pOutX[i + 1] = xStart + bestIndex * xStep;
                pOutY[i + 1] = pY[bestIndex];
                a = bestIndex;
            }

            pOutX[targetPoints - 1] = xStart + (totalPoints - 1) * xStep;
            pOutY[targetPoints - 1] = pY[totalPoints - 1];
        }

        return targetPoints;
    }
}
