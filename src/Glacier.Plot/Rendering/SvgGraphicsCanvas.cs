namespace Glacier.Plot.Rendering;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Glacier.Graphics;
using Glacier.Graphics.Codecs.Png;
using Glacier.Graphics.Raster;
using Glacier.Graphics.Text;
using Glacier.Graphics.Vector;

/// <summary>
/// Pure managed C# vector SVG canvas implementing IGraphicsCanvas.
/// Emits compliant, resolution-independent SVG markup with zero third-party dependencies.
/// </summary>
public sealed class SvgGraphicsCanvas : IGraphicsCanvas
{
    private readonly StringBuilder _sb = new();
    private readonly Stack<int> _saveGroupCounts = new();
    private int _unscopedOpenGroups;
    private int _clipIdCounter;

    public int Width { get; }
    public int Height { get; }

    public SvgGraphicsCanvas(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Clear(Rgba32 color)
    {
        if (color.A == 0) return;
        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<rect width=\"{0}\" height=\"{1}\" fill=\"{2}\" />",
            Width,
            Height,
            FormatColor(color)));
    }

    public void DrawPath(in VectorPath path, in Paint paint)
    {
        if (path == null || path.VerbCount == 0 || paint.Color.A == 0) return;

        string d = ConvertPathToSvgD(path);
        string stroke = FormatColor(paint.Color);
        string join = paint.Join switch
        {
            StrokeJoin.Round => "round",
            StrokeJoin.Bevel => "bevel",
            _ => "miter"
        };
        string cap = paint.Cap switch
        {
            StrokeCap.Round => "round",
            StrokeCap.Square => "square",
            _ => "butt"
        };

        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<path d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"{2:0.##}\" stroke-linejoin=\"{3}\" stroke-linecap=\"{4}\" />",
            d,
            stroke,
            paint.StrokeWidth,
            join,
            cap));
    }

    public void FillPath(in VectorPath path, in Paint paint)
    {
        if (path == null || path.VerbCount == 0 || paint.Color.A == 0) return;

        string d = ConvertPathToSvgD(path);
        string fill = FormatColor(paint.Color);
        string rule = path.FillRule == WindingRule.EvenOdd ? "evenodd" : "nonzero";

        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<path d=\"{0}\" fill=\"{1}\" fill-rule=\"{2}\" />",
            d,
            fill,
            rule));
    }

    public void DrawText(ReadOnlySpan<char> text, float x, float y, in Font font, in Paint paint)
    {
        if (text.IsEmpty || paint.Color.A == 0) return;

        string escaped = SecurityElement.Escape(text.ToString()) ?? string.Empty;
        string escapedFamily = SecurityElement.Escape(font.FamilyName) ?? "Sans-Serif";
        string fill = FormatColor(paint.Color);
        string fontWeight = font.Bold ? "font-weight=\"bold\" " : string.Empty;
        string fontStyle = font.Italic ? "font-style=\"italic\" " : string.Empty;

        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<text x=\"{0:0.##}\" y=\"{1:0.##}\" font-family=\"{2}\" font-size=\"{3:0.##}\" {4}{5}fill=\"{6}\">{7}</text>",
            x,
            y,
            escapedFamily,
            font.Size,
            fontWeight,
            fontStyle,
            fill,
            escaped));
    }

    public void DrawImage(in ReadOnlySpan2D<Rgba32> image, float x, float y, float width, float height)
    {
        if (image.Width <= 0 || image.Height <= 0 || width <= 0f || height <= 0f) return;

        using var fb = new LinearFramebuffer(image.Width, image.Height);
        for (int r = 0; r < image.Height; r++)
        {
            image.GetRowSpan(r).CopyTo(fb.GetRowSpan(r));
        }

        byte[] pngBytes = PngEncoder.Encode(fb);
        string base64 = Convert.ToBase64String(pngBytes);

        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<image x=\"{0:0.##}\" y=\"{1:0.##}\" width=\"{2:0.##}\" height=\"{3:0.##}\" preserveAspectRatio=\"none\" style=\"image-rendering:pixelated\" href=\"data:image/png;base64,{4}\" />",
            x,
            y,
            width,
            height,
            base64));
    }

    public void Save()
    {
        _saveGroupCounts.Push(1);
        _sb.AppendLine("<g>");
    }

    public void Restore()
    {
        if (_saveGroupCounts.Count > 0)
        {
            int count = _saveGroupCounts.Pop();
            for (int i = 0; i < count; i++)
            {
                _sb.AppendLine("</g>");
            }
        }
        else if (_unscopedOpenGroups > 0)
        {
            _unscopedOpenGroups--;
            _sb.AppendLine("</g>");
        }
    }

    public void ClipPath(in VectorPath path)
    {
        if (path == null || path.VerbCount == 0) return;

        string clipId = $"clip_{++_clipIdCounter}";
        string d = ConvertPathToSvgD(path);

        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<defs><clipPath id=\"{0}\"><path d=\"{1}\" /></clipPath></defs>",
            clipId,
            d));
        _sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<g clip-path=\"url(#{0})\">",
            clipId));

        if (_saveGroupCounts.Count > 0)
        {
            _saveGroupCounts.Push(_saveGroupCounts.Pop() + 1);
        }
        else
        {
            _unscopedOpenGroups++;
        }
    }

    public void Flush() { }

    public void Dispose()
    {
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">",
            Width,
            Height));
        sb.Append(_sb.ToString());

        int unclosed = _unscopedOpenGroups;
        foreach (int c in _saveGroupCounts) unclosed += c;
        for (int i = 0; i < unclosed; i++)
        {
            sb.AppendLine("</g>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    public void SaveTo(Stream stream)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(ToString());
        writer.Flush();
    }

    private static string FormatColor(Rgba32 c)
    {
        if (c.A == 255)
        {
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        float a = c.A / 255.0f;
        return string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.##})", c.R, c.G, c.B, a);
    }

    private static string ConvertPathToSvgD(VectorPath path)
    {
        var sb = new StringBuilder();
        var verbs = path.Verbs;
        var points = path.Points;
        int ptIdx = 0;

        for (int i = 0; i < verbs.Length; i++)
        {
            switch (verbs[i])
            {
                case PathVerb.MoveTo:
                    var pMove = points[ptIdx++];
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "M {0:0.##} {1:0.##} ", pMove.X, pMove.Y));
                    break;
                case PathVerb.LineTo:
                    var pLine = points[ptIdx++];
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "L {0:0.##} {1:0.##} ", pLine.X, pLine.Y));
                    break;
                case PathVerb.QuadTo:
                    var p1Q = points[ptIdx++];
                    var p2Q = points[ptIdx++];
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "Q {0:0.##} {1:0.##}, {2:0.##} {3:0.##} ", p1Q.X, p1Q.Y, p2Q.X, p2Q.Y));
                    break;
                case PathVerb.CubicTo:
                    var p1C = points[ptIdx++];
                    var p2C = points[ptIdx++];
                    var p3C = points[ptIdx++];
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "C {0:0.##} {1:0.##}, {2:0.##} {3:0.##}, {4:0.##} {5:0.##} ", p1C.X, p1C.Y, p2C.X, p2C.Y, p3C.X, p3C.Y));
                    break;
                case PathVerb.Close:
                    sb.Append("Z ");
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }
}
