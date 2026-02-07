using System.Runtime.InteropServices;
using Gleam.Engine.Frames;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;
using SkiaSharp;

namespace Gleam.Gestures.Processing;

internal static class MediaPipeFramePreprocessor
{
    public static PreparedFrame? Prepare(PluginFrameContext context)
    {
        return Prepare(context.Frame, context.Frame.TimestampNs);
    }

    public static PreparedFrame? Prepare(RawFrame frame)
    {
        return Prepare(frame, frame.TimestampNs);
    }

    private static PreparedFrame? Prepare(RawFrame frame, long timestampNs)
    {
        var pixelFormat = frame.PixelFormat;

        if (string.Equals(pixelFormat, "RGB24", StringComparison.OrdinalIgnoreCase))
        {
            return new PreparedFrame(
                frame.Width,
                frame.Height,
                "RGB24",
                timestampNs,
                frame.Data);
        }

        if (string.Equals(pixelFormat, "BGRA32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pixelFormat, "ARGB32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pixelFormat, "RGB32", StringComparison.OrdinalIgnoreCase))
        {
            var converted = ConvertBgraLikeToRgb24(frame.Data, frame.Width, frame.Height);
            return new PreparedFrame(
                frame.Width,
                frame.Height,
                "RGB24",
                timestampNs,
                converted);
        }

        if (TryDecodeEncodedToRgb24(frame.Data, out var decodedWidth, out var decodedHeight, out var decodedRgb))
        {
            return new PreparedFrame(
                decodedWidth,
                decodedHeight,
                "RGB24",
                timestampNs,
                decodedRgb);
        }

        if (timestampNs % 300 == 0)
        {
            PluginLogger.Log(
                $"GestureProcessingPlugin: format '{pixelFormat}' is not yet supported in MediaPipe preprocessing.");
        }

        return null;
    }

    private static byte[] ConvertBgraLikeToRgb24(byte[] source, int width, int height)
    {
        var sourceStride = width * 4;
        var expectedSourceLength = sourceStride * height;
        if (source.Length < expectedSourceLength)
        {
            throw new InvalidOperationException(
                $"Invalid frame: expected at least {expectedSourceLength} bytes for 32bpp format, got {source.Length}.");
        }

        var destination = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var sourceRowOffset = y * sourceStride;
            var destinationRowOffset = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var sourceIndex = sourceRowOffset + (x * 4);
                var destinationIndex = destinationRowOffset + (x * 3);
                var b = source[sourceIndex];
                var g = source[sourceIndex + 1];
                var r = source[sourceIndex + 2];

                destination[destinationIndex] = r;
                destination[destinationIndex + 1] = g;
                destination[destinationIndex + 2] = b;
            }
        }

        return destination;
    }

    private static bool TryDecodeEncodedToRgb24(byte[] encodedData, out int width, out int height, out byte[] rgbData)
    {
        width = 0;
        height = 0;
        rgbData = Array.Empty<byte>();

        try
        {
            using var decoded = SKBitmap.Decode(encodedData);
            if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return false;
            }

            width = decoded.Width;
            height = decoded.Height;

            using var normalized = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(normalized))
            {
                canvas.DrawBitmap(decoded, 0, 0);
            }

            var byteCount = normalized.ByteCount;
            var pixelPtr = normalized.GetPixels();
            if (pixelPtr == IntPtr.Zero || byteCount <= 0)
            {
                return false;
            }

            var bgraData = new byte[byteCount];
            Marshal.Copy(pixelPtr, bgraData, 0, byteCount);
            rgbData = ConvertBgraLikeToRgb24(bgraData, width, height);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
