namespace Glacier.Plot.Statistical.Kernels;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

/// <summary>
/// High-speed SIMD-accelerated Gaussian Kernel Density Estimation (KDE) engine.
/// Utilizes Cody-Waite range reduction with a degree-5 Minimax polynomial approximation for exp(x).
/// </summary>
public static unsafe class KdeKernels
{
    private const float C1 = 0.693359375f;
    private const float C2 = -2.1219444005469058e-4f;
    private const float Log2e = 1.4426950408889634f;
    private const float UnderflowThreshold = -87.33f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FastExpScalar(float x)
    {
        if (x < UnderflowThreshold) return 0f;
        if (x > 0f) x = 0f;

        float z = x * Log2e;
        int k = (int)MathF.Round(z);
        float r = (x - k * C1) - k * C2;

        float p = 1.0f + r * (1.0f + r * (0.5f + r * (0.16666667f + r * (0.041666668f + r * 0.008333333f))));
        float scale = BitConverter.Int32BitsToSingle((k + 127) << 23);
        return p * scale;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<float> FastExpAvx512(Vector512<float> x)
    {
        var vUnderflow = Vector512.Create(UnderflowThreshold);
        var vZero = Vector512<float>.Zero;
        var vClamped = Vector512.Min(Vector512.Max(x, vUnderflow), vZero);

        var z = Vector512.Multiply(vClamped, Vector512.Create(Log2e));
        var k = Vector512.ConvertToInt32(Vector512.Round(z));
        var kSingle = Vector512.ConvertToSingle(k);

        var r = Vector512.Subtract(
            Vector512.Subtract(vClamped, Vector512.Multiply(kSingle, Vector512.Create(C1))),
            Vector512.Multiply(kSingle, Vector512.Create(C2))
        );

        var c5 = Vector512.Create(0.008333333f);
        var c4 = Vector512.Create(0.041666668f);
        var c3 = Vector512.Create(0.16666667f);
        var c2 = Vector512.Create(0.5f);
        var c1 = Vector512.Create(1.0f);

        var p = Vector512.Add(c4, Vector512.Multiply(r, c5));
        p = Vector512.Add(c3, Vector512.Multiply(r, p));
        p = Vector512.Add(c2, Vector512.Multiply(r, p));
        p = Vector512.Add(c1, Vector512.Multiply(r, p));
        p = Vector512.Add(c1, Vector512.Multiply(r, p));

        var expBits = Vector512.ShiftLeft(Vector512.Add(k, Vector512.Create(127)), 23);
        var scale = Vector512.AsSingle(expBits);

        var result = Vector512.Multiply(p, scale);
        return Vector512.ConditionalSelect(Vector512.LessThan(x, vUnderflow), vZero, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> FastExpAvx2(Vector256<float> x)
    {
        var vUnderflow = Vector256.Create(UnderflowThreshold);
        var vZero = Vector256<float>.Zero;
        var vClamped = Vector256.Min(Vector256.Max(x, vUnderflow), vZero);

        var z = Vector256.Multiply(vClamped, Vector256.Create(Log2e));
        var k = Vector256.ConvertToInt32(Vector256.Round(z));
        var kSingle = Vector256.ConvertToSingle(k);

        var r = Vector256.Subtract(
            Vector256.Subtract(vClamped, Vector256.Multiply(kSingle, Vector256.Create(C1))),
            Vector256.Multiply(kSingle, Vector256.Create(C2))
        );

        var c5 = Vector256.Create(0.008333333f);
        var c4 = Vector256.Create(0.041666668f);
        var c3 = Vector256.Create(0.16666667f);
        var c2 = Vector256.Create(0.5f);
        var c1 = Vector256.Create(1.0f);

        var p = Vector256.Add(c4, Vector256.Multiply(r, c5));
        p = Vector256.Add(c3, Vector256.Multiply(r, p));
        p = Vector256.Add(c2, Vector256.Multiply(r, p));
        p = Vector256.Add(c1, Vector256.Multiply(r, p));
        p = Vector256.Add(c1, Vector256.Multiply(r, p));

        var expBits = Vector256.ShiftLeft(Vector256.Add(k, Vector256.Create(127)), 23);
        var scale = Vector256.AsSingle(expBits);

        var result = Vector256.Multiply(p, scale);
        return Vector256.ConditionalSelect(Vector256.LessThan(x, vUnderflow), vZero, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<float> FastExpAdvSimd(Vector128<float> x)
    {
        var vUnderflow = Vector128.Create(UnderflowThreshold);
        var vZero = Vector128<float>.Zero;
        var vClamped = Vector128.Min(Vector128.Max(x, vUnderflow), vZero);

        var z = Vector128.Multiply(vClamped, Vector128.Create(Log2e));
        var k = Vector128.ConvertToInt32(Vector128.Round(z));
        var kSingle = Vector128.ConvertToSingle(k);

        var r = Vector128.Subtract(
            Vector128.Subtract(vClamped, Vector128.Multiply(kSingle, Vector128.Create(C1))),
            Vector128.Multiply(kSingle, Vector128.Create(C2))
        );

        var c5 = Vector128.Create(0.008333333f);
        var c4 = Vector128.Create(0.041666668f);
        var c3 = Vector128.Create(0.16666667f);
        var c2 = Vector128.Create(0.5f);
        var c1 = Vector128.Create(1.0f);

        var p = Vector128.Add(c4, Vector128.Multiply(r, c5));
        p = Vector128.Add(c3, Vector128.Multiply(r, p));
        p = Vector128.Add(c2, Vector128.Multiply(r, p));
        p = Vector128.Add(c1, Vector128.Multiply(r, p));
        p = Vector128.Add(c1, Vector128.Multiply(r, p));

        var expBits = Vector128.ShiftLeft(Vector128.Add(k, Vector128.Create(127)), 23);
        var scale = Vector128.AsSingle(expBits);

        var result = Vector128.Multiply(p, scale);
        return Vector128.ConditionalSelect(Vector128.LessThan(x, vUnderflow), vZero, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void VectorizedKde(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        float bandwidth,
        Span<float> outDensity,
        Glacier.Plot.Statistical.Core.GpuTarget target)
        => Glacier.Plot.Statistical.Compute.GpuStatsAccelerator.EvaluateKde(samples, grid, bandwidth, outDensity, target);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void VectorizedKde(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> grid,
        float bandwidth,
        Span<float> outDensity)
    {
        if (bandwidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandwidth), "Bandwidth must be positive.");

        int numSamples = samples.Length;
        int numGrid = grid.Length;
        if (numSamples == 0 || numGrid == 0) return;

        float invH2 = 1.0f / (2.0f * bandwidth * bandwidth);
        float normFactor = 1.0f / (numSamples * bandwidth * MathF.Sqrt(2.0f * MathF.PI));

        fixed (float* pSamples = samples)
        fixed (float* pGrid = grid)
        fixed (float* pOut = outDensity)
        {
            if (numGrid >= 8 && (long)numSamples * numGrid >= 50_000)
            {
                IntPtr ptrSamples = (IntPtr)pSamples;
                IntPtr ptrGrid = (IntPtr)pGrid;
                IntPtr ptrOut = (IntPtr)pOut;

                Parallel.For(0, numGrid, g =>
                {
                    float* pG = (float*)ptrGrid;
                    float* pS = (float*)ptrSamples;
                    float* pO = (float*)ptrOut;

                    float gridVal = pG[g];
                    float sum = EvaluateSingleGridPoint(pS, numSamples, gridVal, invH2);
                    pO[g] = sum * normFactor;
                });
            }
            else
            {
                for (int g = 0; g < numGrid; g++)
                {
                    float gridVal = pGrid[g];
                    float sum = EvaluateSingleGridPoint(pSamples, numSamples, gridVal, invH2);
                    pOut[g] = sum * normFactor;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float EvaluateSingleGridPoint(float* pSamples, int numSamples, float gridVal, float invH2)
    {
        float sum = 0f;
        int s = 0;

        if (Avx512F.IsSupported && numSamples >= 16)
        {
            var vGrid = Vector512.Create(gridVal);
            var vNegInvH2 = Vector512.Create(-invH2);
            var acc0 = Vector512<float>.Zero;
            var acc1 = Vector512<float>.Zero;

            for (; s <= numSamples - 32; s += 32)
            {
                var diff0 = Vector512.Subtract(vGrid, Vector512.Load(pSamples + s));
                var diff1 = Vector512.Subtract(vGrid, Vector512.Load(pSamples + s + 16));

                var exp0 = Vector512.Multiply(Vector512.Multiply(diff0, diff0), vNegInvH2);
                var exp1 = Vector512.Multiply(Vector512.Multiply(diff1, diff1), vNegInvH2);

                acc0 = Vector512.Add(acc0, FastExpAvx512(exp0));
                acc1 = Vector512.Add(acc1, FastExpAvx512(exp1));
            }

            if (s <= numSamples - 16)
            {
                var diff = Vector512.Subtract(vGrid, Vector512.Load(pSamples + s));
                var exp = Vector512.Multiply(Vector512.Multiply(diff, diff), vNegInvH2);
                acc0 = Vector512.Add(acc0, FastExpAvx512(exp));
                s += 16;
            }

            sum += Vector512.Sum(Vector512.Add(acc0, acc1));
        }
        else if (Avx2.IsSupported && numSamples >= 8)
        {
            var vGrid = Vector256.Create(gridVal);
            var vNegInvH2 = Vector256.Create(-invH2);
            var acc0 = Vector256<float>.Zero;
            var acc1 = Vector256<float>.Zero;

            for (; s <= numSamples - 16; s += 16)
            {
                var diff0 = Vector256.Subtract(vGrid, Vector256.Load(pSamples + s));
                var diff1 = Vector256.Subtract(vGrid, Vector256.Load(pSamples + s + 8));

                var exp0 = Vector256.Multiply(Vector256.Multiply(diff0, diff0), vNegInvH2);
                var exp1 = Vector256.Multiply(Vector256.Multiply(diff1, diff1), vNegInvH2);

                acc0 = Vector256.Add(acc0, FastExpAvx2(exp0));
                acc1 = Vector256.Add(acc1, FastExpAvx2(exp1));
            }

            if (s <= numSamples - 8)
            {
                var diff = Vector256.Subtract(vGrid, Vector256.Load(pSamples + s));
                var exp = Vector256.Multiply(Vector256.Multiply(diff, diff), vNegInvH2);
                acc0 = Vector256.Add(acc0, FastExpAvx2(exp));
                s += 8;
            }

            sum += Vector256.Sum(Vector256.Add(acc0, acc1));
        }
        else if (AdvSimd.IsSupported && numSamples >= 4)
        {
            var vGrid = Vector128.Create(gridVal);
            var vInvH2 = Vector128.Create(invH2);
            var acc = Vector128<float>.Zero;

            for (; s <= numSamples - 4; s += 4)
            {
                var diff = Vector128.Subtract(vGrid, Vector128.Load(pSamples + s));
                var diffSq = Vector128.Multiply(diff, diff);
                var exponent = Vector128.Multiply(Vector128.Negate(diffSq), vInvH2);
                acc = Vector128.Add(acc, FastExpAdvSimd(exponent));
            }
            sum += Vector128.Sum(acc);
        }

        for (; s < numSamples; s++)
        {
            float diff = gridVal - pSamples[s];
            float exponent = -(diff * diff) * invH2;
            sum += FastExpScalar(exponent);
        }

        return sum;
    }

    public static float SilvermanBandwidth(ReadOnlySpan<float> samples, float stdDev, float iqr)
    {
        int n = samples.Length;
        if (n <= 1) return 1.0f;

        float effectiveStd = stdDev;
        if (iqr > 0)
        {
            effectiveStd = Math.Min(stdDev, iqr / 1.34f);
        }

        if (effectiveStd <= 0) effectiveStd = 1.0f;

        return 0.9f * effectiveStd * MathF.Pow(n, -0.2f);
    }

    public static float ScottBandwidth(int n, float stdDev)
    {
        if (n <= 1) return 1.0f;
        float s = stdDev > 0 ? stdDev : 1.0f;
        return 1.06f * s * MathF.Pow(n, -0.2f);
    }
}
