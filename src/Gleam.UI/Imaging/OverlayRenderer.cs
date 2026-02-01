using Gleam.Engine.Overlays;
using SkiaSharp;

namespace Gleam.Ui.Imaging;

public sealed class OverlayRenderer
{
    public void Render(SKCanvas canvas, OverlayScene scene)
    {
        foreach (var primitive in scene.Primitives)
        {
            switch (primitive)
            {
                case OverlayLine line:
                    DrawLine(canvas, line);
                    break;
                case OverlayRectangle rectangle:
                    DrawRectangle(canvas, rectangle);
                    break;
                case OverlayCircle circle:
                    DrawCircle(canvas, circle);
                    break;
                case OverlayPolyline polyline:
                    DrawPolyline(canvas, polyline);
                    break;
            }
        }
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

    private static void DrawLine(SKCanvas canvas, OverlayLine line)
    {
        using var paint = BuildStroke(line.Stroke);
        canvas.DrawLine(line.Start.X, line.Start.Y, line.End.X, line.End.Y, paint);
    }

    private static void DrawRectangle(SKCanvas canvas, OverlayRectangle rectangle)
    {
        var rect = new SKRect(rectangle.TopLeft.X, rectangle.TopLeft.Y,
            rectangle.TopLeft.X + rectangle.Width, rectangle.TopLeft.Y + rectangle.Height);

        if (rectangle.Fill.Color.A > 0)
        {
            using var fill = BuildFill(rectangle.Fill);
            canvas.DrawRect(rect, fill);
        }

        using var stroke = BuildStroke(rectangle.Stroke);
        canvas.DrawRect(rect, stroke);
    }

    private static void DrawCircle(SKCanvas canvas, OverlayCircle circle)
    {
        if (circle.Fill.Color.A > 0)
        {
            using var fill = BuildFill(circle.Fill);
            canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, fill);
        }

        using var stroke = BuildStroke(circle.Stroke);
        canvas.DrawCircle(circle.Center.X, circle.Center.Y, circle.Radius, stroke);
    }

    private static void DrawPolyline(SKCanvas canvas, OverlayPolyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return;
        }

        using var paint = BuildStroke(polyline.Stroke);
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