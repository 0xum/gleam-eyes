namespace Gleam.Engine.Overlays;

public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static ColorRgba FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

    public static ColorRgba Transparent => new(0, 0, 0, 0);

    public static ColorRgba White => new(255, 255, 255, 255);

    public static ColorRgba Black => new(0, 0, 0, 255);
}