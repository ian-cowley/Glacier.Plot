namespace Glacier.Plot.Statistical.Grammar;

using System;
using System.Collections.Generic;
using System.IO;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Vector;
using Glacier.Plot.Core;
using Glacier.Plot.Figures;
using Glacier.Plot.Interop;
using Glacier.Plot.Plottables;
using Glacier.Plot.Rendering;
using Glacier.Polaris;
using Glacier.Polaris.Data;
using Glacier.Plot.Statistical;
using Glacier.Plot.Statistical.Kernels;

/// <summary>
/// Declarative Grammar of Graphics chart builder for statistical visualization.
/// Directly consumes Glacier.Polaris DataFrames and compiles into Glacier.Plot figures.
/// </summary>
public sealed class Chart
{
    private readonly DataFrame _df;
    private string? _xCol;
    private string? _yCol;
    private string? _colorCol;
    private string? _sizeCol;
    private string? _facetRowCol;
    private string? _facetColCol;

    private readonly List<IPlottable> _layers = new();
    private readonly List<Action<Chart>> _facetActions = new();
    private readonly Figure _figure = new();

    public Figure UnderlyingFigure => _figure;
    public IReadOnlyList<IPlottable> Layers => _layers;

    private Chart(DataFrame df)
    {
        ArgumentNullException.ThrowIfNull(df);
        _df = df;
    }

    public static Chart FromDataFrame(DataFrame df) => new(df);

    public Chart Encode(string x, string? y = null, string? color = null, string? size = null)
    {
        _xCol = x;
        _yCol = y;
        _colorCol = color;
        _sizeCol = size;

        _figure.XAxis.Label = x;
        if (y != null) _figure.YAxis.Label = y;
        return this;
    }

    public Chart WithTitle(string title)
    {
        _figure.Title = title;
        return this;
    }

    public Chart WithTheme(PlotTheme theme)
    {
        _figure.Theme = theme;
        return this;
    }

    /// <summary>
    /// Partitions the chart into a 2D small-multiples grid across row and/or column categorical series.
    /// </summary>
    public Chart FacetGrid(string? row = null, string? col = null)
    {
        if (string.IsNullOrWhiteSpace(row) && string.IsNullOrWhiteSpace(col))
            throw new ArgumentException("At least one of row or col must be specified for FacetGrid.");

        if (row != null) _ = _df.GetColumn(row);
        if (col != null) _ = _df.GetColumn(col);

        _facetRowCol = row;
        _facetColCol = col;
        return this;
    }

    public Chart GeomViolin(float? bandwidth = null, bool showBox = true)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomViolin requires both X and Y encoding.");

        _facetActions.Add(c => c.GeomViolin(bandwidth, showBox));

        string groupCol = _colorCol ?? _xCol;
        var catSeries = _df.GetColumn(groupCol);
        float[] yVals = _df.GetColumn(_yCol).ToFloatArray();

        var groups = GroupByCategories(catSeries, yVals);
        int categoryIndex = 0;

        foreach (var (catName, samples) in groups)
        {
            if (samples.Length == 0) continue;
            var color = GetPaletteColor(categoryIndex);
            var style = new PlotStyle { Color = color, FillAlpha = 150 };
            var violin = new ViolinPlot(categoryIndex, samples, 0.8f, bandwidth, style)
            {
                Label = catName,
                ShowBox = showBox
            };
            AddLayer(violin);
            categoryIndex++;
        }

        return this;
    }

    public Chart GeomBox(float whiskerMultiplier = 1.5f)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomBox requires both X and Y encoding.");

        _facetActions.Add(c => c.GeomBox(whiskerMultiplier));

        string groupCol = _colorCol ?? _xCol;
        var catSeries = _df.GetColumn(groupCol);
        float[] yVals = _df.GetColumn(_yCol).ToFloatArray();

        var groups = GroupByCategories(catSeries, yVals);
        int categoryIndex = 0;

        foreach (var (catName, samples) in groups)
        {
            if (samples.Length == 0) continue;
            var color = GetPaletteColor(categoryIndex);
            var style = new PlotStyle { Color = color, FillAlpha = 180 };
            var box = new BoxPlot(categoryIndex, samples, 0.6f, whiskerMultiplier, style)
            {
                Label = catName
            };
            AddLayer(box);
            categoryIndex++;
        }

        return this;
    }

    public Chart GeomKde(float? bandwidth = null, bool fill = true)
    {
        if (_xCol == null)
            throw new InvalidOperationException("GeomKde requires X encoding.");

        _facetActions.Add(c => c.GeomKde(bandwidth, fill));

        float[] vals = _df.GetColumn(_xCol).ToFloatArray();
        if (_colorCol != null)
        {
            var groups = GroupByCategories(_df.GetColumn(_colorCol), vals);
            int idx = 0;
            foreach (var (catName, catSamples) in groups)
            {
                if (catSamples.Length == 0) continue;
                var style = new PlotStyle { Color = GetPaletteColor(idx++), IsFilled = fill, FillAlpha = 90 };
                AddLayer(new KdePlot(catSamples, 200, bandwidth, style) { Label = catName });
            }
        }
        else
        {
            var style = new PlotStyle { Color = Colors.Cyan, IsFilled = fill, FillAlpha = 90 };
            AddLayer(new KdePlot(vals, 200, bandwidth, style) { Label = _xCol });
        }

        _figure.YAxis.Label = "Density";
        return this;
    }

    public Chart GeomScatter(float alpha = 0.7f)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("GeomScatter requires both X and Y encoding.");

        _facetActions.Add(c => c.GeomScatter(alpha));

        float[] x = _df.GetColumn(_xCol).ToFloatArray();
        float[] y = _df.GetColumn(_yCol).ToFloatArray();
        byte alphaByte = (byte)Math.Clamp((int)MathF.Round(alpha * 255f), 1, 255);

        float[]? rawSizes = _sizeCol != null ? _df.GetColumn(_sizeCol).ToFloatArray() : null;
        float[]? normalizedRadii = rawSizes != null ? NormalizeRadii(rawSizes, minRadius: 3.0f, maxRadius: 11.0f) : null;

        if (_colorCol != null)
        {
            var indexGroups = GroupIndicesByCategory(_df.GetColumn(_colorCol));
            int catIdx = 0;
            foreach (var (catName, indices) in indexGroups)
            {
                if (indices.Count == 0) continue;
                float[] gx = new float[indices.Count];
                float[] gy = new float[indices.Count];
                float[]? gr = normalizedRadii != null ? new float[indices.Count] : null;
                for (int i = 0; i < indices.Count; i++)
                {
                    int row = indices[i];
                    gx[i] = x[row];
                    gy[i] = y[row];
                    if (gr != null) gr[i] = normalizedRadii![row];
                }

                Rgba32 color = GetPaletteColor(catIdx++).WithAlpha(alphaByte);
                if (gr != null)
                {
                    AddLayer(new BubbleScatterPlottable(gx, gy, gr, color, catName));
                }
                else
                {
                    var style = new PlotStyle { Color = color, Marker = MarkerShape.Circle, MarkerSize = 6.0f };
                    AddLayer(new ScatterPlot(gx, gy, style) { Label = catName });
                }
            }
        }
        else if (normalizedRadii != null)
        {
            Rgba32 color = Colors.Amber.WithAlpha(alphaByte);
            AddLayer(new BubbleScatterPlottable(x, y, normalizedRadii, color, $"{_yCol} vs {_xCol}"));
        }
        else
        {
            var style = new PlotStyle { Color = Colors.Amber.WithAlpha(alphaByte), Marker = MarkerShape.Circle, MarkerSize = 6.0f };
            AddLayer(new ScatterPlot(x, y, style) { Label = $"{_yCol} vs {_xCol}" });
        }

        return this;
    }

    public Chart GeomHistogram(int bins = 30, bool density = false)
    {
        if (_xCol == null)
            throw new InvalidOperationException("GeomHistogram requires X encoding.");

        _facetActions.Add(c => c.GeomHistogram(bins, density));

        float[] vals = _df.GetColumn(_xCol).ToFloatArray();
        var histType = density ? HistogramType.Density : HistogramType.Count;

        if (_colorCol != null)
        {
            var groups = GroupByCategories(_df.GetColumn(_colorCol), vals);
            int idx = 0;
            foreach (var (catName, catSamples) in groups)
            {
                if (catSamples.Length == 0) continue;
                var style = new PlotStyle { Color = GetPaletteColor(idx++), IsFilled = true, FillAlpha = 150 };
                AddLayer(new HistogramPlot(catSamples, bins, histType, style) { Label = catName });
            }
        }
        else
        {
            var style = new PlotStyle { Color = Colors.Emerald, IsFilled = true, FillAlpha = 180 };
            AddLayer(new HistogramPlot(vals, bins, histType, style) { Label = _xCol });
        }

        _figure.YAxis.Label = density ? "Density" : "Count";
        return this;
    }

    public Chart GeomHeatmap()
    {
        _facetActions.Add(c => c.GeomHeatmap());

        var (matrix, names) = CorrelationKernels.ComputeCorrelationMatrix(_df);
        int n = names.Length;

        var heatmap = new HeatmapPlot(matrix, n, n, ColorMap.Coolwarm);
        AddLayer(heatmap);
        _figure.Title ??= "Correlation Matrix";
        _figure.ShowLegend = false;
        return this;
    }

    public Chart AddRegressionTrend(bool showConfidenceBand = true)
    {
        if (_xCol == null || _yCol == null)
            throw new InvalidOperationException("AddRegressionTrend requires both X and Y encoding.");

        _facetActions.Add(c => c.AddRegressionTrend(showConfidenceBand));

        float[] x = _df.GetColumn(_xCol).ToFloatArray();
        float[] y = _df.GetColumn(_yCol).ToFloatArray();
        if (x.Length < 2) return this;

        var style = new PlotStyle { Color = Colors.Cyan, StrokeWidth = 2.5f };
        var reg = new RegressionPlot(x, y, style)
        {
            ShowConfidenceBand = showConfidenceBand,
            ShowScatter = false,
            Label = "OLS Linear Fit"
        };

        AddLayer(reg);
        return this;
    }

    public Figure ToGlacierPlotFigure() => _figure;

    public void RenderToGlacierCanvas(IGraphicsCanvas canvas, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_facetRowCol != null || _facetColCol != null)
        {
            RenderFacetGridToCanvas(canvas, width, height);
            return;
        }

        _figure.Render(canvas, width, height);
    }

    public void RenderGlacierPng(Stream stream, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (_facetRowCol != null || _facetColCol != null)
        {
            using var fb = new LinearFramebuffer(width, height);
            using var canvas = new CpuGraphicsCanvas(fb);
            RenderFacetGridToCanvas(canvas, width, height);
            PngEncoder.Encode(fb, stream);
            return;
        }

        _figure.RenderGlacierPng(stream, width, height);
    }

    public byte[] RenderGlacierPng(int width = 1280, int height = 720)
    {
        using var ms = new MemoryStream();
        RenderGlacierPng(ms, width, height);
        return ms.ToArray();
    }

    public void RenderToPng(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var stream = File.Create(filePath);
        RenderGlacierPng(stream, width, height);
    }

    public void RenderToGlacierPng(string filePath, int width = 1280, int height = 720)
    {
        RenderToPng(filePath, width, height);
    }

    public void RenderToSvg(string filePath, int width = 1280, int height = 720)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        using var svgCanvas = new SvgGraphicsCanvas(width, height);
        RenderToGlacierCanvas(svgCanvas, width, height);
        File.WriteAllText(filePath, svgCanvas.ToString());
    }

    private void AddLayer(IPlottable plottable)
    {
        _layers.Add(plottable);
        _figure.AddPlottable(plottable);
    }

    private Rgba32 GetPaletteColor(int index)
    {
        var palette = _figure.Theme.Palette;
        if (palette == null || palette.Length == 0) return Colors.Cyan;
        return palette[index % palette.Length];
    }

    private void RenderFacetGridToCanvas(IGraphicsCanvas canvas, int width, int height)
    {
        int rowCount = _df.Columns.Count > 0 ? _df.Columns[0].Length : 0;
        var rowGroups = _facetRowCol != null
            ? GroupIndicesByCategory(_df.GetColumn(_facetRowCol))
            : new Dictionary<string, List<int>> { ["*"] = AllRowIndices(rowCount) };

        var colGroups = _facetColCol != null
            ? GroupIndicesByCategory(_df.GetColumn(_facetColCol))
            : new Dictionary<string, List<int>> { ["*"] = AllRowIndices(rowCount) };

        var rowKeys = new List<string>(rowGroups.Keys);
        var colKeys = new List<string>(colGroups.Keys);
        int nRows = Math.Max(1, rowKeys.Count);
        int nCols = Math.Max(1, colKeys.Count);

        canvas.Clear(_figure.Theme.FigureBackground);

        int cellW = Math.Max(64, width / nCols);
        int cellH = Math.Max(64, height / nRows);

        for (int r = 0; r < nRows; r++)
        {
            var rSet = new HashSet<int>(rowGroups[rowKeys[r]]);
            for (int c = 0; c < nCols; c++)
            {
                var cList = colGroups[colKeys[c]];
                var cellIndices = new List<int>();
                for (int i = 0; i < cList.Count; i++)
                {
                    if (rSet.Contains(cList[i])) cellIndices.Add(cList[i]);
                }

                using var cellFb = new LinearFramebuffer(cellW, cellH);
                using (var cellCanvas = new CpuGraphicsCanvas(cellFb))
                {
                    cellCanvas.Clear(_figure.Theme.FigureBackground);
                    if (cellIndices.Count > 0)
                    {
                        DataFrame subDf = SliceDataFrameByIndices(_df, cellIndices);
                        var subChart = FromDataFrame(subDf)
                            .WithTheme(_figure.Theme)
                            .Encode(_xCol ?? string.Empty, _yCol, _colorCol, _sizeCol);

                        string facetTitle = (_facetRowCol != null && _facetColCol != null)
                            ? $"{_facetRowCol}={rowKeys[r]} | {_facetColCol}={colKeys[c]}"
                            : (_facetRowCol != null ? $"{_facetRowCol}={rowKeys[r]}" : $"{_facetColCol}={colKeys[c]}");
                        subChart.WithTitle(facetTitle);

                        foreach (var action in _facetActions)
                        {
                            action(subChart);
                        }

                        subChart.UnderlyingFigure.Render(cellCanvas, cellW, cellH);
                    }
                }

                canvas.DrawImage(cellFb.AsReadOnlySpan2D(), c * cellW, r * cellH, cellW, cellH);
            }
        }

        canvas.Flush();
    }

    private static List<int> AllRowIndices(int count)
    {
        var list = new List<int>(count);
        for (int i = 0; i < count; i++) list.Add(i);
        return list;
    }

    private static DataFrame SliceDataFrameByIndices(DataFrame df, List<int> indices)
    {
        int n = indices.Count;
        int colCount = df.Columns.Count;
        var slicedCols = new ISeries[colCount];
        for (int col = 0; col < colCount; col++)
        {
            ISeries src = df.Columns[col];
            if (src is Float32Series f32)
            {
                var dst = new Float32Series(src.Name, n);
                var dstSpan = dst.Memory.Span;
                var srcSpan = f32.Memory.Span;
                for (int i = 0; i < n; i++) dstSpan[i] = srcSpan[indices[i]];
                slicedCols[col] = dst;
            }
            else if (src is Float64Series f64)
            {
                var dst = new Float64Series(src.Name, n);
                var dstSpan = dst.Memory.Span;
                var srcSpan = f64.Memory.Span;
                for (int i = 0; i < n; i++) dstSpan[i] = srcSpan[indices[i]];
                slicedCols[col] = dst;
            }
            else if (src is Int32Series i32)
            {
                var dst = new Int32Series(src.Name, n);
                var dstSpan = dst.Memory.Span;
                var srcSpan = i32.Memory.Span;
                for (int i = 0; i < n; i++) dstSpan[i] = srcSpan[indices[i]];
                slicedCols[col] = dst;
            }
            else
            {
                string[] strs = new string[n];
                for (int i = 0; i < n; i++) strs[i] = GetCategoryLabelAt(src, indices[i]);
                slicedCols[col] = CategoricalSeries.FromStrings(src.Name, strs);
            }
        }
        return new DataFrame(slicedCols);
    }

    private static string GetCategoryLabelAt(ISeries col, int index)
    {
        if (col is CategoricalSeries cat)
        {
            if (cat.ValidityMask.IsNull(index)) return "Unknown";
            uint code = cat.Memory.Span[index];
            return code < (uint)cat.RevMap.Length ? cat.RevMap[code] : "Unknown";
        }
        if (col is Utf8StringSeries utf8)
        {
            return utf8.ValidityMask.IsNull(index) ? "Unknown" : (utf8.GetString(index) ?? "Unknown");
        }
        return col.Get(index)?.ToString() ?? "Unknown";
    }

    private static float[] NormalizeRadii(float[] values, float minRadius, float maxRadius)
    {
        float[] radii = new float[values.Length];
        if (values.Length == 0) return radii;

        float min = values[0], max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min) min = values[i];
            if (values[i] > max) max = values[i];
        }

        float span = max - min;
        if (span <= 1e-6f)
        {
            Array.Fill(radii, (minRadius + maxRadius) * 0.5f);
            return radii;
        }

        float invSpan = 1.0f / span;
        for (int i = 0; i < values.Length; i++)
        {
            float t = Math.Clamp((values[i] - min) * invSpan, 0f, 1f);
            radii[i] = minRadius + t * (maxRadius - minRadius);
        }
        return radii;
    }

    private static Dictionary<string, List<int>> GroupIndicesByCategory(ISeries catCol)
    {
        int len = catCol.Length;
        var map = new Dictionary<string, List<int>>();
        for (int i = 0; i < len; i++)
        {
            string cat = GetCategoryLabelAt(catCol, i);
            if (!map.TryGetValue(cat, out var list))
            {
                list = new List<int>();
                map[cat] = list;
            }
            list.Add(i);
        }
        return map;
    }

    private static Dictionary<string, float[]> GroupByCategories(ISeries catCol, float[] yVals)
    {
        int len = catCol.Length;
        if (catCol is CategoricalSeries catSeries)
        {
            var revMap = catSeries.RevMap;
            var codes = catSeries.Memory.Span;
            int numCats = revMap.Length;
            int[] counts = new int[numCats + 1];

            for (int i = 0; i < len; i++)
            {
                if (catSeries.ValidityMask.IsNull(i))
                {
                    counts[numCats]++;
                }
                else
                {
                    uint c = codes[i];
                    if (c < (uint)numCats) counts[c]++;
                    else counts[numCats]++;
                }
            }

            var dict = new Dictionary<string, float[]>();
            var offsets = new int[numCats + 1];
            for (int c = 0; c < numCats; c++)
            {
                if (counts[c] > 0)
                {
                    dict[revMap[c]] = new float[counts[c]];
                }
            }
            if (counts[numCats] > 0)
            {
                dict["Unknown"] = new float[counts[numCats]];
            }

            for (int i = 0; i < len; i++)
            {
                if (catSeries.ValidityMask.IsNull(i))
                {
                    dict["Unknown"][offsets[numCats]++] = yVals[i];
                }
                else
                {
                    uint c = codes[i];
                    if (c < (uint)numCats && counts[c] > 0)
                    {
                        dict[revMap[c]][offsets[c]++] = yVals[i];
                    }
                    else
                    {
                        dict["Unknown"][offsets[numCats]++] = yVals[i];
                    }
                }
            }

            return dict;
        }

        var categoryIndexMap = GroupIndicesByCategory(catCol);
        var result = new Dictionary<string, float[]>(categoryIndexMap.Count);
        foreach (var (cat, indices) in categoryIndexMap)
        {
            float[] arr = new float[indices.Count];
            for (int j = 0; j < indices.Count; j++)
            {
                arr[j] = yVals[indices[j]];
            }
            result[cat] = arr;
        }
        return result;
    }

    private sealed class BubbleScatterPlottable : IPlottable
    {
        private readonly float[] _x;
        private readonly float[] _y;
        private readonly float[] _radii;

        public string? Label { get; set; }
        public PlotStyle Style { get; set; }

        public BubbleScatterPlottable(float[] x, float[] y, float[] radii, Rgba32 color, string? label)
        {
            _x = x;
            _y = y;
            _radii = radii;
            Label = label;
            Style = new PlotStyle { Color = color, Marker = MarkerShape.Circle };
        }

        public AxisLimits GetLimits() => _x.Length == 0
            ? AxisLimits.Default
            : AxisLimits.FromData(_x, _y).WithPadding(0.08, 0.08);

        public void Render(IGraphicsCanvas canvas, CoordinateConverter converter, PlotTheme theme)
        {
            if (_x.Length == 0) return;

            var path = new VectorPath();
            for (int i = 0; i < _x.Length; i++)
            {
                float px = converter.GetPixelX(_x[i]);
                float py = converter.GetPixelY(_y[i]);
                path.AddCircle(px, py, _radii[i]);
            }

            canvas.FillPath(path, new Paint(Style.Color, PaintStyle.Fill));
            canvas.DrawPath(path, new Paint(Style.Color.WithAlpha(255), PaintStyle.Stroke, 1.0f));
        }
    }
}

/// <summary>
/// First-party Glacier.Graphics rendering extensions for Chart.
/// </summary>
public static class StatsVizGlacierExtensions
{
    public static void RenderToGlacierCanvas(Chart chart, IGraphicsCanvas canvas, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.RenderToGlacierCanvas(canvas, width, height);
    }

    public static void RenderGlacierPng(Chart chart, Stream stream, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.RenderGlacierPng(stream, width, height);
    }
}
