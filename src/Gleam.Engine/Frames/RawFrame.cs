namespace Gleam.Engine.Frames;

public sealed class RawFrame
{
    public RawFrame(int width, int height, string pixelFormat, byte[] data, long timestampNs)
    {
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        Data = data;
        TimestampNs = timestampNs;
    }

    public int Width { get; }

    public int Height { get; }

    public string PixelFormat { get; }

    public byte[] Data { get; }

    public long TimestampNs { get; }
}