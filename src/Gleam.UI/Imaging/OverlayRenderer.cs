using System.Collections.Generic;
using Gleam.Engine.Overlays;
using SkiaSharp;

namespace Gleam.Ui.Imaging;

public sealed class OverlayRenderer
{
    private const int MaxCachedPaints = 128;
    private readonly Dictionary<OverlayStroke, SKPaint> _strokePaintCache = new();
    private readonly Dictionary<OverlayFill, SKPaint> _fillPaintCache = new();

    public void Render(SKCanvas canvas, OverlayScene scene)
    {
        if (_strokePaintCache.Count > MaxCachedPaints || _fillPaintCache.Count > MaxCachedPaints)
        {
            DisposeAndClearCaches();
        }

        foreach (var primitive in scene.Primitives)
        {
            switch (primitive)
            {
                case OverlayLine line:
                    DrawLine(canvas, line, _strokePaintCache);
                    break;
                case OverlayRectangle rectangle:
                    DrawRectangle(canvas, rectangle, _strokePaintCache, _fillPaintCache);
                    break;
                case OverlayCircle circle:
                    DrawCircle(canvas, circle, _strokePaintCache, _fillPaintCache);
                    break;
                case OverlayPolyline polyline:
                    DrawPolyline(canvas, polyline, _strokePaintCache);
                    break;
            }
        }

    }

    private void DisposeAndClearCaches()
    {
        foreach (var paint in _strokePaintCache.Values)
        {
            paint.Dispose();
        }

        foreach (var paint in _fillPaintCache.Values)
        {
            paint.Dispose();
        }

        _strokePaintCache.Clear();
        _fillPaintCache.Clear();
    }

    private static SKPaint GetStrokePaint(OverlayStroke stroke, Dictionary<OverlayStroke, SKPaint> strokePaintCache)
    {
        if (strokePaintCache.TryGetValue(stroke, out var cached))
        {
            return cached;
        }

        var paint = BuildStroke(stroke);
        strokePaintCache[stroke] = paint;
        return paint;
    }

    private static SKPaint GetFillPaint(OverlayFill fill, Dictionary<OverlayFill, SKPaint> fillPaintCache)
    {
        if (fillPaintCache.TryGetValue(fill, out var cached))
        {
            return cached;
        }

        var paint = BuildFill(fill);
        fillPaintCache[fill] = paint;
        return paint;
    }

    private static SKPaint BuildStroke(OverlayStroke stroke)
        => new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke.Thickness,
            Color = ToSkiaColor(stroke.Color)
        };

    private static SKPaint BuildFill(OverlayFill fill)
        => new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = ToSkiaColor(fill.Color)
        };

    private static void DrawLine(SKCanvas canvas, OverlayLine line, Dictionary<OverlayStroke, SKPaint> strokePaintCache)
    {
        var paint = GetStrokePaint(line.Stroke, strokePaintCache);
        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
    }

    private static void DrawRectangle(
        SKCanvas canvas,
        OverlayRectangle rectangle,
        Dictionary<OverlayStroke, SKPaint> strokePaintCache,
        Dictionary<OverlayFill, SKPaint> fillPaintCache)
    {
        var rect = new SKRect(rectangle.TopLeft.X, rectangle.TopLeft.Y,
            rectangle.TopLeft.X + rectangle.Width, rectangle.TopLeft.Y + rectangle.Height);

        if (rectangle.Fill.Color.A > 0)
        {
            var fill = GetFillPaint(rectangle.Fill, fillPaintCache);
            canvas.DrawRect(rect, fill);
        }

        var stroke = GetStrokePaint(rectangle.Stroke, strokePaintCache);
        canvas.DrawRect(rect, stroke);
    }

    private static void DrawCircle(
        SKCanvas canvas,
        OverlayCircle circle,
        Dictionary<OverlayStroke, SKPaint> strokePaintCache,
        Dictionary<OverlayFill, SKPaint> fillPaintCache)
    {
        if (circle.Fill.Color.A > 0)
        {
            var fill = GetFillPaint(circle.Fill, fillPaintCache);
            canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, fill);
        }

        var stroke = GetStrokePaint(circle.Stroke, strokePaintCache);
        canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, stroke);
    }

    private static void DrawPolyline(
        SKCanvas canvas,
        OverlayPolyline polyline,
        Dictionary<OverlayStroke, SKPaint> strokePaintCache)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        var paint = GetStrokePaint(polyline.Stroke, strokePaintCache);
        using var path = new SKPath();
        path.MoveTo(polyline.Points[0].X, polyline.Points[0].Y);
        for (var i = 1; i < polyline.Points.Count; i++)
        {
            path.LineTo(polyline.Points[i].X, polyline.Points[i].Y);
        }
        canvas.DrawPath(path, paint);
    }

    private static SKColor ToSkiaColor(ColorRgba color)
        => new(color.R, color.G, color.B, color.A);
}