namespace Glacier.Plot.Tests;

using Glacier.Plot.Figures;
using Glacier.Plot.Interop;
using Glacier.Plot.Statistical.Kernels;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.Tensor.Core;
using Xunit;

public class InteropTests
{
    [Fact]
    public void Polaris_DataFrame_PlotsDirectly()
    {
        var timeSeries = new Float32Series("Time", 5);
        new float[] { 0f, 1f, 2f, 3f, 4f }.CopyTo(timeSeries.Memory.Span);

        var valSeries = new Float32Series("Value", 5);
        new float[] { 10f, 25f, 18f, 32f, 29f }.CopyTo(valSeries.Memory.Span);

        var df = new DataFrame([timeSeries, valSeries]);

        using var fig = df.PlotLine("Time", "Value", "Polaris Line Chart");
        byte[] png = fig.RenderToBytes(500, 350);

        Assert.NotNull(png);
        Assert.True(png.Length > 200);
        Assert.Equal(0x89, png[0]);
    }

    [Fact]
    public void Polaris_Float32Series_ScatterAndHistogram_RenderCleanly()
    {
        var x = new Float32Series("X", 64);
        var y = new Float32Series("Y", 64);
        for (int i = 0; i < 64; i++)
        {
            x.Memory.Span[i] = i * 0.25f;
            y.Memory.Span[i] = i * 0.5f + (i % 5);
        }

        var df = new DataFrame([x, y]);
        using var scatterFig = df.PlotScatter("X", "Y", "Polaris Scatter");
        byte[] scatterPng = scatterFig.RenderToBytes(400, 300);
        Assert.Equal(0x89, scatterPng[0]);

        using var histFig = df.PlotHistogram("Y", bins: 10, "Polaris Histogram");
        byte[] histPng = histFig.RenderToBytes(400, 300);
        Assert.Equal(0x89, histPng[0]);

        var (matrix, names) = CorrelationKernels.ComputeCorrelationMatrix(df);
        Assert.Equal(2, names.Length);
        Assert.Equal(1.0f, matrix[0], 3);
        Assert.True(matrix[1] > 0.9f);
    }

    [Fact]
    public void Tensor_PlotsDirectly_AsSignalAndHeatmap()
    {
        // 1D tensor
        using var lossCurve = Tensor<float>.FromSpan([2.5f, 1.8f, 1.2f, 0.7f, 0.3f, 0.1f], [6]);
        using var fig = new Figure { Title = "Training Loss" };
        fig.PlotLine(lossCurve, "Loss");

        byte[] pngLine = fig.RenderToBytes(400, 300);
        Assert.NotEmpty(pngLine);

        // 2D tensor
        using var weights = Tensor<float>.FromSpan(
            [
                0.1f, 0.5f, 0.9f,
                0.3f, 0.8f, 0.2f
            ],
            [2, 3]);

        using var heatFig = new Figure { Title = "Weights Matrix" };
        heatFig.PlotHeatmap(weights);

        byte[] pngHeat = heatFig.RenderToBytes(400, 300);
        Assert.NotEmpty(pngHeat);
    }
}
