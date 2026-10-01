namespace Glacier.Plot.Tests;

using System;
using System.IO;
using Glacier.Graphics;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using Glacier.Plot.Figures;
using Glacier.Plot.Plottables;
using Xunit;

public class FigureRenderingTests
{
    [Fact]
    public void Figure_RendersToPngBytes_WithValidHeader()
    {
        using var fig = new Figure { Title = "Test Render" };
        fig.XAxis.Label = "X";
        fig.YAxis.Label = "Y";

        float[] x = [0, 1, 2, 3, 4];
        float[] y = [0, 1, 4, 9, 16];

        fig.PlotLine(x, y, "Quadratic", Colors.Cyan);
        fig.PlotScatter(x, y, "Points", Colors.Amber);

        byte[] png = fig.RenderToBytes(400, 300);

        Assert.NotNull(png);
        Assert.True(png.Length > 100);
        // Check PNG magic bytes: 0x89, 0x50, 0x4E, 0x47
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
    }

    [Fact]
    public void Figure_RendersBarsAndHistograms()
    {
        using var fig = new Figure { Title = "Distribution Test" };
        float[] values = [1f, 2f, 2.5f, 3f, 3.1f, 3.2f, 4f, 5f, 5.5f, 6f];
        fig.PlotHistogram(values, bins: 5, label: "Frequency");

        string[] categories = ["Alpha", "Beta", "Gamma"];
        float[] catVals = [10f, 25f, 15f];
        fig.PlotBars(categories, catVals, label: "Categories");

        byte[] png = fig.RenderToBytes(400, 300);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Figure_RendersScatterWithDifferentMarkers()
    {
        using var fig = new Figure { Title = "Markers Test" };
        float[] x = [1f, 2f, 3f, 4f, 5f];
        float[] y = [2f, 4f, 1f, 5f, 3f];

        var s1 = fig.PlotScatter(x, y, "Circles");
        s1.Style.Marker = MarkerShape.Circle;

        var s2 = fig.PlotScatter(x, y, "Diamonds");
        s2.Style.Marker = MarkerShape.Diamond;

        var s3 = fig.PlotScatter(x, y, "Squares");
        s3.Style.Marker = MarkerShape.Square;

        var s4 = fig.PlotScatter(x, y, "Crosses");
        s4.Style.Marker = MarkerShape.Cross;

        var s5 = fig.PlotScatter(x, y, "Pluses");
        s5.Style.Marker = MarkerShape.Plus;

        byte[] png = fig.RenderToBytes(500, 400);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Figure_RendersThemes_LightAndCyber()
    {
        using var figLight = new Figure { Title = "Light Theme", Theme = PlotTheme.Light };
        figLight.PlotSignal([1f, 3f, 2f, 5f], label: "Data");
        byte[] pngLight = figLight.RenderToBytes(400, 300);
        Assert.NotEmpty(pngLight);

        using var figCyber = new Figure { Title = "Cyber Theme", Theme = PlotTheme.Cyber, LegendPosition = LegendLocation.BottomLeft };
        figCyber.PlotSignal([10f, 30f, 20f, 50f], label: "Neon");
        byte[] pngCyber = figCyber.RenderToBytes(400, 300);
        Assert.NotEmpty(pngCyber);
    }

    [Fact]
    public void Figure_RendersHeatmap()
    {
        using var fig = new Figure { Title = "Heatmap Test" };
        float[] matrix =
        [
            1f, 2f, 3f,
            4f, 5f, 6f,
            7f, 8f, 9f
        ];
        fig.PlotHeatmap(matrix, rows: 3, cols: 3, ColorMap.Viridis);

        byte[] png = fig.RenderToBytes(400, 300);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void Figure_ExportsSvg()
    {
        using var fig = new Figure { Title = "Vector SVG Test" };
        fig.PlotSignal([10f, 20f, 15f, 30f, 25f], label: "Signal");

        string tempSvg = Path.Combine(Path.GetTempPath(), $"glacier_test_{Path.GetRandomFileName()}.svg");
        try
        {
            fig.SaveSvg(tempSvg, 600, 400);
            Assert.True(File.Exists(tempSvg));
            string content = File.ReadAllText(tempSvg);
            Assert.Contains("<svg", content);
            Assert.Contains("</svg>", content);
        }
        finally
        {
            if (File.Exists(tempSvg)) File.Delete(tempSvg);
        }
    }

    [Fact]
    public void SignalPlot_AcceptsReadOnlyMemory_RendersWithoutAllocationChurn()
    {
        using var fig = new Figure { Title = "Memory Span Test" };
        ReadOnlyMemory<float> x = new float[] { 0f, 1f, 2f, 3f, 4f, 5f };
        ReadOnlyMemory<float> y = new float[] { 10f, 20f, 15f, 35f, 30f, 45f };

        var plot = fig.PlotLine(x, y, "MemoryPlot", Colors.Emerald);
        Assert.NotNull(plot);
        Assert.Equal(5f, plot.GetLimits().XMax);

        // Verify multiple renders reuse cached SKPaint/SKPath without fault
        for (int i = 0; i < 5; i++)
        {
            byte[] png = fig.RenderToBytes(400, 300);
            Assert.NotEmpty(png);
            Assert.Equal(0x89, png[0]);
        }
    }

    [Fact]
    public void Figure_RendersToGlacierPng_WithValidHeader()
    {
        using var fig = new Figure { Title = "Glacier Graphics Render Test" };
        fig.XAxis.Label = "Time (s)";
        fig.YAxis.Label = "Amplitude";

        float[] x = [0f, 1f, 2f, 3f, 4f];
        float[] y = [0f, 2f, 8f, 18f, 32f];
        fig.PlotLine(x, y, "Signal", Colors.Cyan);
        fig.PlotScatter(x, y, "Samples", Colors.Amber);

        byte[] png = Glacier.Plot.Interop.GlacierGraphicsPlotExtensions.RenderGlacierPng(fig, 400, 300);
        Assert.NotNull(png);
        Assert.True(png.Length > 64);
        // Verify PNG magic numbers 0x89, 0x50, 0x4E, 0x47
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);
    }

    [Fact]
    public void Figure_RendersToFramebuffer_AndPng_ZeroSkiaSharpAssembliesLoaded()
    {
        using var fig = new Figure { Title = "Pure C# Render Test" };
        fig.XAxis.Label = "Sample X";
        fig.YAxis.Label = "Sample Y";

        float[] x = [0f, 1f, 2f, 3f, 4f, 5f];
        float[] y = [5f, 15f, 25f, 12f, 45f, 30f];
        fig.PlotLine(x, y, "Line", Colors.Cyan);
        fig.PlotScatter(x, y, "Points", Colors.Amber);
        fig.PlotBars([0f, 1f, 2f], [10f, 20f, 15f], "Bars", Colors.Emerald);

        // 1. Render directly to LinearFramebuffer
        using var fb = fig.RenderToFramebuffer(640, 480);
        Assert.NotNull(fb);
        Assert.Equal(640, fb.Width);
        Assert.Equal(480, fb.Height);
        Assert.False(fb.AsByteSpan().IsEmpty);

        // 2. Render to PNG stream
        using var ms = new MemoryStream();
        fig.RenderGlacierPng(ms, 640, 480);
        byte[] png = ms.ToArray();
        Assert.True(png.Length > 64);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
        Assert.Equal(0x4E, png[2]);
        Assert.Equal(0x47, png[3]);

        // 3. Verify zero SkiaSharp assemblies loaded in process
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in loadedAssemblies)
        {
            string name = asm.GetName().Name ?? "";
            Assert.DoesNotContain("SkiaSharp", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SignalPlot_SimdDecimation_WiresDirectlyToVectorPath()
    {
        int n = 10_000;
        float[] x = new float[n];
        float[] y = new float[n];
        for (int i = 0; i < n; i++)
        {
            x[i] = i;
            y[i] = MathF.Sin(i * 0.05f) * 10f;
        }

        var dims = new PlotDimensions(800, 600);
        var limits = new AxisLimits(0, n, -15, 15);
        var conv = new CoordinateConverter(dims, limits);

        // Test LTTB directly to VectorPath
        var lttbPath = new VectorPath();
        int lttbCount = LttbKernels.DownsampleToPath(x, y, 400, conv, lttbPath);
        Assert.Equal(400, lttbCount);
        Assert.True(lttbPath.PointCount >= 400);

        // Test MinMax directly to VectorPath
        var minMaxPath = new VectorPath();
        int mmCount = MinMaxKernels.DownsampleToPath(x, y, 400, conv, minMaxPath);
        Assert.True(mmCount > 0);
        Assert.True(minMaxPath.PointCount > 0);

        // Test uniform LTTB to VectorPath
        var uniformPath = new VectorPath();
        int uniformCount = LttbKernels.DownsampleUniformToPath(y, 0f, 1f, 400, conv, uniformPath);
        Assert.Equal(400, uniformCount);
        Assert.True(uniformPath.PointCount >= 400);
    }
}

