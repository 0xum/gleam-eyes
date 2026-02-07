namespace Gleam.Gestures.Models;

internal sealed record PreparedFrame(
    int Width,
    int Height,
    string PixelFormat,
    long TimestampNs,
    byte[] Data);
