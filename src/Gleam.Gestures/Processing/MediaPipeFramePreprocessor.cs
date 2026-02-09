using Gleam.Engine.Frames;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;
using SkiaSharp;

namespace Gleam.Gestures.Processing;

internal static class MediaPipeFramePreprocessor
{
    private const bool DownscaleForInference = true;
    private static int _inferenceMaxWidth = 1280;
    private static int _inferenceMaxHeight = 720;

    public static void SetInferenceDownscaleTarget(int maxWidth, int maxHeight)
    {
        var width = Math.Max(1, maxWidth);
        var height = Math.Max(1, maxHeight);
        System.Threading.Volatile.Write(ref _inferenceMaxWidth, width);
        System.Threading.Volatile.Write(ref _inferenceMaxHeight, height);
    }

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
            var (resizedWidth, resizedHeight, resizedData) = MaybeResizeRgb24(frame.Data, frame.Width, frame.Height);
            return new PreparedFrame(
                resizedWidth,
                resizedHeight,
                "RGB24",
                timestampNs,
                resizedData);
        }

        if (string.Equals(pixelFormat, "BGRA32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pixelFormat, "ARGB32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pixelFormat, "RGB32", StringComparison.OrdinalIgnoreCase))
        {
            var converted = ConvertBgraLikeToRgb24(frame.Data, frame.Width, frame.Height);
            var (resizedWidth, resizedHeight, resizedData) = MaybeResizeRgb24(converted, frame.Width, frame.Height);
            return new PreparedFrame(
                resizedWidth,
                resizedHeight,
                "RGB24",
                timestampNs,
                resizedData);
        }

        if (TryDecodeEncodedToRgb24(frame.Data, out var decodedWidth, out var decodedHeight, out var decodedRgb))
        {
            var (resizedWidth, resizedHeight, resizedData) = MaybeResizeRgb24(decodedRgb, decodedWidth, decodedHeight);
            return new PreparedFrame(
                resizedWidth,
                resizedHeight,
                "RGB24",
                timestampNs,
                resizedData);
        }

        return null;
    }

    private static byte[] ConvertBgraLikeToRgb24(byte[] source, int width, int height)
    {
        var destination = new byte[width * height * 3];

        var pixelCount = width * height;
        for (var i = 0; i < pixelCount; i++)
        {
            var srcIdx = i << 2;
            var dstIdx = i * 3;
            destination[dstIdx] = source[srcIdx + 2];     // R
            destination[dstIdx + 1] = source[srcIdx + 1]; // G
            destination[dstIdx + 2] = source[srcIdx];     // B
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

            var pixelPtr = normalized.GetPixels();
            if (pixelPtr == IntPtr.Zero)
            {
                return false;
            }

            rgbData = new byte[width * height * 3];
            unsafe
            {
                var src = (byte*)pixelPtr.ToPointer();
                for (var i = 0; i < width * height; i++)
                {
                    var srcIdx = i << 2;
                    var dstIdx = i * 3;
                    rgbData[dstIdx] = src[srcIdx + 2];
                    rgbData[dstIdx + 1] = src[srcIdx + 1];
                    rgbData[dstIdx + 2] = src[srcIdx];
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (int Width, int Height, byte[] Data) MaybeResizeRgb24(byte[] source, int sourceWidth, int sourceHeight)
    {
        if (!DownscaleForInference || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (sourceWidth, sourceHeight, source);
        }

        var inferenceMaxWidth = System.Threading.Volatile.Read(ref _inferenceMaxWidth);
        var inferenceMaxHeight = System.Threading.Volatile.Read(ref _inferenceMaxHeight);
        var widthScale = inferenceMaxWidth / (float)sourceWidth;
        var heightScale = inferenceMaxHeight / (float)sourceHeight;
        var scale = Math.Min(1f, Math.Min(widthScale, heightScale));

        if (scale >= 1f)
        {
            return (sourceWidth, sourceHeight, source);
        }

        var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return (targetWidth, targetHeight, ResizeRgb24Nearest(source, sourceWidth, sourceHeight, targetWidth, targetHeight));
    }

    private static byte[] ResizeRgb24Nearest(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var destination = new byte[targetWidth * targetHeight * 3];

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = y * sourceHeight / targetHeight;
            var dstRowOffset = y * targetWidth * 3;
            var srcRowOffset = sourceY * sourceWidth * 3;

            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = x * sourceWidth / targetWidth;
                var srcIdx = srcRowOffset + sourceX * 3;
                var dstIdx = dstRowOffset + x * 3;

                destination[dstIdx] = source[srcIdx];
                destination[dstIdx + 1] = source[srcIdx + 1];
                destination[dstIdx + 2] = source[srcIdx + 2];
            }
        }

        return destination;
    }
}
