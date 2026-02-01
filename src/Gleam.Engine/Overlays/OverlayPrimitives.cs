namespace Gleam.Engine.Overlays;

public readonly record struct OverlayPoint(float X, float Y)
{
    public static OverlayPoint FromPixels(float x, float y) => new(x, y);
}

public readonly record struct OverlayStroke(float Thickness, ColorRgba Color)
{
    public static OverlayStroke Default => new(2f, ColorRgba.White);
}

public readonly record struct OverlayFill(ColorRgba Color)
{
    public static OverlayFill Transparent => new(ColorRgba.Transparent);
}

public sealed record OverlayLine(OverlayPoint Start, OverlayPoint End, OverlayStroke Stroke)
    : OverlayPrimitive;

public sealed record OverlayRectangle(OverlayPoint TopLeft, float Width, float Height, OverlayStroke Stroke,
    OverlayFill Fill) : OverlayPrimitive;

public sealed record OverlayCircle(OverlayPoint Center, float Radius, OverlayStroke Stroke, OverlayFill Fill)
    : OverlayPrimitive;

public sealed record OverlayPolyline(IReadOnlyList<OverlayPoint> Points, OverlayStroke Stroke)
    : OverlayPrimitive;

public abstract record OverlayPrimitive;