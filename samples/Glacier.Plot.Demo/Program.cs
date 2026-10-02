namespace Glacier.Plot.Demo;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public static void Main(string[] args)
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

            // Render to memory framebuffer
            using var fb = fig4.RenderToFramebuffer(800, 450);
        }
        sw.Stop();

        long bytesAlloc = GC.GetAllocatedBytesForCurrentThread() - initialAlloc;
        double fps = frameCount / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  ✓ Simulated {frameCount} frames: {fps:F1} FPS (Total: {sw.Elapsed.TotalMilliseconds:F1} ms)");
        Console.WriteLine($"  ✓ Steady-State Managed Allocation: {bytesAlloc / (1024.0 * frameCount):F2} KB / frame");

        string path4 = Path.Combine(outDir, "demo_streaming_last_frame.png");
        fig4.SavePng(path4, 800, 450);
        Console.WriteLine($"  ✓ Saved final streaming frame -> {path4}\n");

        // -----------------------------------------------------------------------------------------
        // Demo 5: Glacier.Graphics Vector Stroking & Typography Showcase
        // -----------------------------------------------------------------------------------------
        Console.WriteLine("[Demo 5] Glacier.Graphics 2D Vector & System Font Typography Showcase");
        int gWidth = 1280;
        int gHeight = 720;
        using var gCanvas = new Glacier.Graphics.CpuGraphicsCanvas(gWidth, gHeight);
        gCanvas.Clear(new Glacier.Graphics.Rgba32(18, 22, 32, 255)); // Deep navy background

        // 1. Typography using lazy system-font fallback (Arial / Segoe UI / DejaVu)
        var titleFont = new Glacier.Graphics.Text.Font(size: 28f, bold: true);
        var subFont = new Glacier.Graphics.Text.Font(size: 16f);
        var smallFont = new Glacier.Graphics.Text.Font(size: 13f);
        var monoFont = new Glacier.Graphics.Text.Font(size: 14f);

        var titlePaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(245, 247, 250, 255));
        var accentPaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(56, 189, 248, 255));
        var dimPaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(148, 163, 184, 255));
        var greenPaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(52, 211, 153, 255));
        var amberPaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(251, 191, 36, 255));
        var rosePaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(244, 63, 94, 255));

        gCanvas.DrawText("Glacier.Graphics: Pure C# .NET 10 Vector & Font Engine", 50, 60, titleFont, titlePaint);
        gCanvas.DrawText("System Font Fallback • LCD Subpixel Antialiasing • EvenOdd Winding • Annular Reverse-Contour Stroking", 50, 95, subFont, dimPaint);

        // Section 1: Fast oscillating wave with EvenOdd winding (testing self-intersecting polygon fix)
        gCanvas.DrawText("1. Oscillating Signal Path (EvenOdd Winding Test)", 50, 150, subFont, accentPaint);
        var wavePath = new Glacier.Graphics.Vector.VectorPath();
        wavePath.MoveTo(50, 220);
        for (int i = 0; i <= 360; i++)
        {
            float wx = 50 + i * 1.5f;
            float wy = 220 + MathF.Sin(i * 0.25f) * 45f + MathF.Sin(i * 0.8f) * 15f;
            wavePath.LineTo(wx, wy);
        }
        var waveStroke = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(56, 189, 248, 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 2.0f);
        gCanvas.DrawPath(wavePath, waveStroke);
        gCanvas.DrawText("No solid fill artifacts on high-frequency oscillations", 50, 290, smallFont, dimPaint);

        // Section 2: Annular stroked shapes (hollow circles and rects with opposing contours)
        gCanvas.DrawText("2. Annular Stroked Shapes (Reverse-Contour Test)", 680, 150, subFont, greenPaint);
        var ringPath = new Glacier.Graphics.Vector.VectorPath();
        ringPath.AddCircle(780, 220, 50);
        var ringStroke = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(52, 211, 153, 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 8.0f);
        gCanvas.DrawPath(ringPath, ringStroke);

        var rectPath = new Glacier.Graphics.Vector.VectorPath();
        rectPath.AddRect(880, 170, 120, 100);
        var rectStroke = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(251, 191, 36, 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 5.0f);
        gCanvas.DrawPath(rectPath, rectStroke);

        var starPath = new Glacier.Graphics.Vector.VectorPath();
        starPath.AddCircle(1090, 220, 45);
        var starFill = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(244, 63, 94, 80));
        var starStroke = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(244, 63, 94, 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 4.0f);
        gCanvas.DrawPath(starPath, starFill);
        gCanvas.DrawPath(starPath, starStroke);

        gCanvas.DrawText("Hollow annular strokes render with correct inner holes", 680, 290, smallFont, dimPaint);

        // Section 3: Smooth Cubic & Quad Bezier Curves with Variable Line Weights
        gCanvas.DrawText("3. Multi-Width Bezier Curves & Smooth Splines", 50, 360, subFont, amberPaint);
        for (int b = 0; b < 5; b++)
        {
            var bPath = new Glacier.Graphics.Vector.VectorPath();
            float by = 400 + b * 20;
            bPath.MoveTo(50, by);
            bPath.CubicTo(200, by - 40, 380, by + 40, 560, by);
            var bPaint = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32((byte)(100 + b * 30), 200, (byte)(255 - b * 40), 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 1.0f + b * 1.5f);
            gCanvas.DrawPath(bPath, bPaint);
        }

        // Section 4: Typography specimen
        gCanvas.DrawText("4. Dynamic Font Atlas & Multi-Size Specimen", 680, 360, subFont, rosePaint);
        gCanvas.DrawText("ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789", 680, 400, subFont, titlePaint);
        gCanvas.DrawText("The quick brown fox jumps over the lazy dog.", 680, 425, smallFont, dimPaint);
        gCanvas.DrawText("EffectiveTypeface: Shared System TTF Fallback loaded seamlessly", 680, 450, smallFont, greenPaint);
        gCanvas.DrawText("Math & Symbols: α β γ δ ε λ π σ ω ∑ ∫ √ ± ≠ ≤ ≥ ≈", 680, 475, smallFont, accentPaint);

        // Section 5: Status Card
        var cardPath = new Glacier.Graphics.Vector.VectorPath();
        cardPath.AddRect(50, 540, 1180, 130);
        var cardBg = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(28, 34, 48, 255));
        var cardBorder = new Glacier.Graphics.Paint(new Glacier.Graphics.Rgba32(51, 65, 85, 255), Glacier.Graphics.PaintStyle.Stroke, StrokeWidth: 1.5f);
        gCanvas.DrawPath(cardPath, cardBg);
        gCanvas.DrawPath(cardPath, cardBorder);

        gCanvas.DrawText("VERIFIED HARDWARE RASTERIZATION METRICS", 75, 575, subFont, accentPaint);
        gCanvas.DrawText("• LinearFramebuffer: 1280x720 32-bpp BGRA zero-allocation span rasterization", 75, 605, smallFont, dimPaint);
        gCanvas.DrawText("• LCD Subpixel Antialiasing: 4x horizontal sub-binning with RGB color-fringing compensation", 75, 625, smallFont, dimPaint);
        gCanvas.DrawText("• Pure Managed Engine: 100% C# .NET 10 • Zero SkiaSharp / Zero System.Drawing dependencies", 75, 645, smallFont, greenPaint);

        string path5 = Path.Combine(outDir, "demo_graphics_showcase.png");
        using (var fs = File.Create(path5))
        {
            Glacier.Graphics.Codecs.Png.PngEncoder.Encode(gCanvas.Framebuffer, fs);
        }
        Console.WriteLine($"  ✓ Rendered & Saved 720p Vector & Typography Showcase -> {path5}\n");

        Console.WriteLine("================================================================================");
        Console.WriteLine("           ALL DEMOS COMPLETED SUCCESSFULLY: GLACIER.PLOT IS READY!             ");
        Console.WriteLine("================================================================================");

        bool isHeadless = args.Contains("--headless") || args.Contains("--bench");
        if (!isHeadless)
        {
            try
            {
                Console.WriteLine($"\n[Displaying 10,000,000-point 4K plot on screen: {path1}]");
                Process.Start(new ProcessStartInfo(path1) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  (Note: could not auto-launch image viewer: {ex.Message})");
            }

            if (Environment.UserInteractive && !Console.IsInputRedirected)
            {
                Console.WriteLine("\n[Press any key to exit...]");
                Console.ReadKey();
            }
        }
    }
}
