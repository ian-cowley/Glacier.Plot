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
    private readonly StringBuilder _pathBuilder = new(256);
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
        _sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{Width}\" height=\"{Height}\" fill=\"");
        AppendColor(_sb, color);
        _sb.AppendLine("\" />");
    }

    public void DrawPath(in VectorPath path, in Paint paint)
    {
        if (path == null || path.VerbCount == 0 || paint.Color.A == 0) return;

        string d = ConvertPathToSvgD(path);
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

        _sb.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"none\" stroke=\"");
        AppendColor(_sb, paint.Color);
        _sb.AppendLine(CultureInfo.InvariantCulture, $"\" stroke-width=\"{paint.StrokeWidth:0.##}\" stroke-linejoin=\"{join}\" stroke-linecap=\"{cap}\" />");
    }

    public void FillPath(in VectorPath path, in Paint paint)
    {
        if (path == null || path.VerbCount == 0 || paint.Color.A == 0) return;

        string d = ConvertPathToSvgD(path);
        string rule = path.FillRule == WindingRule.EvenOdd ? "evenodd" : "nonzero";

        _sb.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"");
        AppendColor(_sb, paint.Color);
        _sb.AppendLine($"\" fill-rule=\"{rule}\" />");
    }

    public void DrawText(ReadOnlySpan<char> text, float x, float y, in Font font, in Paint paint)
    {
        if (text.IsEmpty || paint.Color.A == 0) return;

        string escaped = SecurityElement.Escape(text.ToString()) ?? string.Empty;
        string escapedFamily = SecurityElement.Escape(font.FamilyName) ?? "Sans-Serif";
        string fontWeight = font.Bold ? "font-weight=\"bold\" " : string.Empty;
        string fontStyle = font.Italic ? "font-style=\"italic\" " : string.Empty;

        _sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{x:0.##}\" y=\"{y:0.##}\" font-family=\"{escapedFamily}\" font-size=\"{font.Size:0.##}\" {fontWeight}{fontStyle}fill=\"");
        AppendColor(_sb, paint.Color);
        _sb.AppendLine($"\">{escaped}</text>");
    }

    internal void DrawTextRotated(ReadOnlySpan<char> text, float x, float y, float angleDegrees, in Font font, in Paint paint)
    {
        if (text.IsEmpty || paint.Color.A == 0) return;

        string escaped = SecurityElement.Escape(text.ToString()) ?? string.Empty;
        string escapedFamily = SecurityElement.Escape(font.FamilyName) ?? "Sans-Serif";
        string fontWeight = font.Bold ? "font-weight=\"bold\" " : string.Empty;
        string fontStyle = font.Italic ? "font-style=\"italic\" " : string.Empty;

        _sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{x:0.##}\" y=\"{y:0.##}\" text-anchor=\"middle\" transform=\"rotate({angleDegrees:0.##} {x:0.##} {y:0.##})\" font-family=\"{escapedFamily}\" font-size=\"{font.Size:0.##}\" {fontWeight}{fontStyle}fill=\"");
        AppendColor(_sb, paint.Color);
        _sb.AppendLine($"\">{escaped}</text>");
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

        _sb.AppendLine(CultureInfo.InvariantCulture,
            $"<image x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{width:0.##}\" height=\"{height:0.##}\" preserveAspectRatio=\"none\" style=\"image-rendering:pixelated\" href=\"data:image/png;base64,{base64}\" />");
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

        int id = ++_clipIdCounter;
        string d = ConvertPathToSvgD(path);

        _sb.AppendLine(CultureInfo.InvariantCulture, $"<defs><clipPath id=\"clip_{id}\"><path d=\"{d}\" /></clipPath></defs>");
        _sb.AppendLine(CultureInfo.InvariantCulture, $"<g clip-path=\"url(#clip_{id})\">");

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
        _pathBuilder.Clear();
    }

    public override string ToString()
    {
        var sb = new StringBuilder(_sb.Length + 128);
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">");
        sb.Append(_sb);

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

    private static void AppendColor(StringBuilder sb, Rgba32 c)
    {
        if (c.A == 255)
        {
            sb.Append(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            return;
        }
        float a = c.A / 255.0f;
        sb.Append(CultureInfo.InvariantCulture, $"rgba({c.R},{c.G},{c.B},{a:0.##})");
    }

    private string ConvertPathToSvgD(VectorPath path)
    {
        _pathBuilder.Clear();
        var verbs = path.Verbs;
        var points = path.Points;
        int ptIdx = 0;

        for (int i = 0; i < verbs.Length; i++)
        {
            switch (verbs[i])
            {
                case PathVerb.MoveTo:
                    var pMove = points[ptIdx++];
                    _pathBuilder.Append(CultureInfo.InvariantCulture, $"M {pMove.X:0.##} {pMove.Y:0.##} ");
                    break;
                case PathVerb.LineTo:
                    var pLine = points[ptIdx++];
                    _pathBuilder.Append(CultureInfo.InvariantCulture, $"L {pLine.X:0.##} {pLine.Y:0.##} ");
                    break;
                case PathVerb.QuadTo:
                    var p1Q = points[ptIdx++];
                    var p2Q = points[ptIdx++];
                    _pathBuilder.Append(CultureInfo.InvariantCulture, $"Q {p1Q.X:0.##} {p1Q.Y:0.##}, {p2Q.X:0.##} {p2Q.Y:0.##} ");
                    break;
                case PathVerb.CubicTo:
                    var p1C = points[ptIdx++];
                    var p2C = points[ptIdx++];
                    var p3C = points[ptIdx++];
                    _pathBuilder.Append(CultureInfo.InvariantCulture, $"C {p1C.X:0.##} {p1C.Y:0.##}, {p2C.X:0.##} {p2C.Y:0.##}, {p3C.X:0.##} {p3C.Y:0.##} ");
                    break;
                case PathVerb.Close:
                    _pathBuilder.Append("Z ");
                    break;
            }
        }

        if (_pathBuilder.Length > 0 && _pathBuilder[^1] == ' ')
            _pathBuilder.Length--;

        return _pathBuilder.ToString();
    }
}
