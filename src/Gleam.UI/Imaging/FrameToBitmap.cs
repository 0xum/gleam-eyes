using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gleam.Engine.Frames;

namespace Gleam.Ui.Imaging;

public static class FrameToBitmap
{
    public static Bitmap Convert(RawFrame frame)
    {
        if (string.Equals(frame.PixelFormat, "BGRA32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(frame.PixelFormat, "ARGB32", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(frame.PixelFormat, "RGB32", StringComparison.OrdinalIgnoreCase))
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            using (var buffer = bitmap.Lock())
            {
                Marshal.Copy(frame.Data, 0, buffer.Address, frame.Data.Length);
            }

            return bitmap;
        }

        // FlashCap can deliver BMP/JPEG/PNG bytes depending on device format.
        using var stream = new MemoryStream(frame.Data, writable: false);
        return new Bitmap(stream);
    }
}