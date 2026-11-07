namespace Glacier.Plot.Tests;

using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.Plot.Statistical.Kernels;
using Xunit;

public class CorrelationTests
{
    [Fact]
    public void PearsonCorrelation_IdentifiesLinearRelationships()
    {
        float[] x = [1f, 2f, 3f, 4f, 5f];
        float[] yPos = [2f, 4f, 6f, 8f, 10f];
        float[] yNeg = [-1f, -2f, -3f, -4f, -5f];
        float[] yZero = [5f, -5f, 5f, -5f, 5f];

        float rPos = CorrelationKernels.PearsonCorrelation(x, yPos);
        float rNeg = CorrelationKernels.PearsonCorrelation(x, yNeg);
        float rZero = CorrelationKernels.PearsonCorrelation(x, yZero);

        Assert.Equal(1.0f, rPos, 3);
        Assert.Equal(-1.0f, rNeg, 3);
        Assert.True(Math.Abs(rZero) < 0.2f);
    }

    [Fact]
    public void CorrelationMatrix_FromDataFrame_IsSymmetricWithUnitDiagonal()
    {
        var s1 = new Float32Series("A", 4);
        var s2 = new Float32Series("B", 4);
        new float[] { 1f, 2f, 3f, 4f }.CopyTo(s1.Memory.Span);
        new float[] { 2f, 4f, 5f, 7f }.CopyTo(s2.Memory.Span);

        var df = new DataFrame([s1, s2]);
        var (matrix, names) = CorrelationKernels.ComputeCorrelationMatrix(df);

        Assert.Equal(2, names.Length);
        Assert.Equal(4, matrix.Length); // 2x2

        // Diagonals must be 1.0
        Assert.Equal(1.0f, matrix[0], 3);
        Assert.Equal(1.0f, matrix[3], 3);

        // Off-diagonals must match (symmetric)
        Assert.Equal(matrix[1], matrix[2], 3);
        Assert.True(matrix[1] > 0.95f); // High positive correlation
    }
}
