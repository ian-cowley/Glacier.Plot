namespace Glacier.Plot.Demo;

using System;
using System.Diagnostics;
using System.IO;
using Glacier.Plot.Core;
using Glacier.Plot.Decimation;
using Glacier.Plot.Figures;
using Glacier.Plot.Interop;
using Glacier.Plot.Plottables;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.Tensor.Core;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("          GLACIER.PLOT: HIGH-PERFORMANCE 2D VISUALIZATION ENGINE (.NET 10)     ");
        Console.WriteLine("                    Native C# Alternative to Python Matplotlib                  ");
        Console.WriteLine("================================================================================\n");

        string outDir = Path.Combine(AppContext.BaseDirectory, "output");
        Directory.CreateDirectory(outDir);

        // -----------------------------------------------------------------------------------------
        // Demo 1: Massive 10,000,000-Point Signal Plot with SIMD LTTB Decimation
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 1] 10,000,000-Point Telemetry Signal (SIMD LTTB Downsampling)");
        int pointCount = 10_000_000;
        Console.WriteLine($"  Generating {pointCount:N0} telemetry data points...");

        float[] signalData = new float[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            // Combined carrier sine wave + harmonics + stochastic noise + periodic spike transients
            float t = i * 0.0001f;
            float v = MathF.Sin(t * 10f) * 10f + MathF.Sin(t * 150f) * 2f;
            if (i % 500_000 == 0 && i > 0) v += 35f; // simulated voltage spike
            signalData[i] = v;
        }

        int targetPixels = 1920;
        float[] outX = new float[targetPixels];
        float[] outY = new float[targetPixels];

        // Warmup
        LttbKernels.DownsampleUniform(signalData.AsSpan(0, 1000), 0f, 1f, 100, outX, outY);

        var sw = Stopwatch.StartNew();
        int downsampled = LttbKernels.DownsampleUniform(signalData, 0f, 0.0001f, targetPixels, outX, outY);
        sw.Stop();

        double lttbMs = sw.Elapsed.TotalMilliseconds;
        double throughput = (pointCount / 1_000_000.0) / (lttbMs / 1000.0);
        Console.WriteLine($"  ✓ LTTB Decimation: {pointCount:N0} pts -> {downsampled} pts in {lttbMs:F2} ms ({throughput:F1} Million pts/sec)");

        using var fig1 = new Figure
        {
            Title = $"High-Voltage Grid Telemetry ({pointCount:N0} Points)",
            Theme = PlotTheme.Cyber
        };
        fig1.XAxis.Label = "Time (seconds)";
        fig1.YAxis.Label = "Potential (kV)";
        var line1 = fig1.PlotSignal(signalData, xStart: 0f, xStep: 0.0001f, label: "Bus Voltage", color: Colors.Cyan);
        line1.Style.StrokeWidth = 1.5f;

        string path1 = Path.Combine(outDir, "demo_telemetry_10m.png");
        sw.Restart();
        fig1.SavePng(path1, 1920, 1080);
        sw.Stop();
        Console.WriteLine($"  ✓ Rendered & Saved 1080p PNG in {sw.Elapsed.TotalMilliseconds:F2} ms -> {path1}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 2: Glacier.Polaris DataFrame Interop & Multi-Series Financial Plot
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 2] Glacier.Polaris DataFrame Zero-Copy Financial Chart");
        int days = 500;
        var daySeries = new Float32Series("Day", days);
        var assetASeries = new Float32Series("Asset_A", days);
        var assetBSeries = new Float32Series("Asset_B", days);

        float priceA = 100f;
        float priceB = 80f;
        var rand = new Random(42);

        var spanDays = daySeries.Memory.Span;
        var spanA = assetASeries.Memory.Span;
        var spanB = assetBSeries.Memory.Span;

        for (int i = 0; i < days; i++)
        {
            spanDays[i] = i;
            priceA += (float)(rand.NextDouble() - 0.49) * 4f;
            priceB += (float)(rand.NextDouble() - 0.48) * 3f;
            spanA[i] = priceA;
            spanB[i] = priceB;
        }

        var df = new DataFrame([daySeries, assetASeries, assetBSeries]);
        using var fig2 = new Figure
        {
            Title = "Dual-Asset Price Series (Direct from Polaris.DataFrame)",
            Theme = PlotTheme.Dark
        };
        fig2.XAxis.Label = "Trading Days";
        fig2.YAxis.Label = "Asset Price (USD)";
        var lA = fig2.PlotLine(df.GetColumn("Day"), df.GetColumn("Asset_A"), "Equities Index", Colors.Emerald);
        lA.Style.StrokeWidth = 2.5f;
        var lB = fig2.PlotLine(df.GetColumn("Day"), df.GetColumn("Asset_B"), "Tech Index", Colors.Amber);
        lB.Style.StrokeWidth = 2.0f;
        lB.Style.Pattern = LinePattern.Dashed;

        string path2 = Path.Combine(outDir, "demo_polaris_chart.png");
        fig2.SavePng(path2, 1280, 720);
        Console.WriteLine($"  ✓ Rendered Polaris multi-series chart -> {path2}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 3: Glacier.Tensor 2D Weight Heatmap & Autograd Loss Curve
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 3] Glacier.Tensor Deep Learning Matrix Heatmap & Loss Curves");
        int matrixDim = 32;
        using var weightTensor = Tensor<float>.Zeros([matrixDim, matrixDim]);
        var spanWeights = weightTensor.AsSpan();
        for (int r = 0; r < matrixDim; r++)
        {
            for (int c = 0; c < matrixDim; c++)
            {
                float dist = MathF.Sqrt((r - 16) * (r - 16) + (c - 16) * (c - 16));
                spanWeights[r * matrixDim + c] = MathF.Cos(dist * 0.4f) * MathF.Exp(-dist * 0.08f);
            }
        }

        using var fig3 = new Figure
        {
            Title = "Convolutional Filter Attention Heatmap (Glacier.Tensor)",
            Theme = PlotTheme.Dark,
            ShowLegend = false
        };
        fig3.XAxis.Label = "Filter X";
        fig3.YAxis.Label = "Filter Y";
        fig3.PlotHeatmap(weightTensor, ColorMap.Plasma);

        string path3 = Path.Combine(outDir, "demo_tensor_heatmap.png");
        fig3.SavePng(path3, 800, 800);
        Console.WriteLine($"  ✓ Rendered 2D Tensor Colormap Heatmap -> {path3}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 4: Zero-Allocation 120 FPS Streaming Simulation
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 4] Zero-Allocation Real-Time Telemetry Streaming Simulation (120 FPS)");
        using var fig4 = new Figure
        {
            Title = "Real-Time Sensor Ring-Buffer Stream",
            Theme = PlotTheme.Cyber
        };
        fig4.XAxis.Label = "Sample Window (Samples)";
        fig4.YAxis.Label = "Sensor Amplitude";
        using var streamPlot = fig4.PlotStream(capacity: 2000, label: "ECG Sensor", color: Colors.NeonPink);

        // Simulate high-frequency data ingestion and 120 frames of continuous rendering
        sw.Restart();
        long initialAlloc = GC.GetAllocatedBytesForCurrentThread();

        int frameCount = 120;
        for (int f = 0; f < frameCount; f++)
        {
            // Push 50 new samples per frame (6000 samples/sec)
            for (int s = 0; s < 50; s++)
            {
                int idx = f * 50 + s;
                float val = MathF.Sin(idx * 0.05f) * 5f + ((idx % 40 == 0) ? 20f : 0f);
                streamPlot.Append(val);
            }

            // Render to memory image
            using var img = fig4.RenderToImage(800, 450);
        }
        sw.Stop();

        long bytesAlloc = GC.GetAllocatedBytesForCurrentThread() - initialAlloc;
        double fps = frameCount / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  ✓ Simulated {frameCount} frames: {fps:F1} FPS (Total: {sw.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine($"  ✓ Steady-State Managed Allocation: {bytesAlloc / (1024.0 * frameCount):F2} KB / frame");

        string path4 = Path.Combine(outDir, "demo_streaming_last_frame.png");
        fig4.SavePng(path4, 800, 450);
        Console.WriteLine($"  ✓ Saved final streaming frame -> {path4}\n");

        Console.WriteLine("================================================================================");
        Console.WriteLine("           ALL DEMOS COMPLETED SUCCESSFULLY: GLACIER.PLOT IS READY!             ");
        Console.WriteLine("================================================================================");
    }
}
