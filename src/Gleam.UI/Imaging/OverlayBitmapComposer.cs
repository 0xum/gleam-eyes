using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;
using SkiaSharp;

namespace Gleam.Ui.Imaging;

public sealed class OverlayBitmapComposer
{
    private readonly OverlayRenderer _renderer = new();
    private WriteableBitmap? _bitmap;
    private SKBitmap? _skBitmap;
    private int _width;
    private int _height;

    public WriteableBitmap Compose(in RawFrame frame, OverlayScene scene)
    {
        EnsureBuffers(frame.Width, frame.Height);
        if (_bitmap == null || _skBitmap == null)
        {
            throw new InvalidOperationException("Failed to allocate buffers.");
        }

        using (var buffer = _bitmap.Lock())
        {
            var ptr = buffer.Address;
            if (ptr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to lock bitmap buffer.");
            }

            if (string.Equals(frame.PixelFormat, "BGRA32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(frame.PixelFormat, "ARGB32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(frame.PixelFormat, "RGB32", StringComparison.OrdinalIgnoreCase))
            {
                Marshal.Copy(frame.Data, 0, ptr, frame.Data.Length);
            }
            else
            {
                using var decoded = SKBitmap.Decode(frame.Data);
                if (decoded != null)
                {
                    var decodedPtr = decoded.GetPixels();
                    if (decodedPtr != IntPtr.Zero)
                    {
                        var byteCount = Math.Min(decoded.ByteCount, buffer.RowBytes * frame.Height);
                        unsafe
                        {
                            Buffer.MemoryCopy((void*)decodedPtr, (void*)ptr, byteCount, byteCount);
                        }
                    }
                }
            }

            _skBitmap.InstallPixels(_skBitmap.Info, ptr, buffer.RowBytes);
            using var canvas = new SKCanvas(_skBitmap);
            _renderer.Render(canvas, scene);
        }

        return _bitmap;
    }

    private void EnsureBuffers(int width, int height)
    {
        if (_bitmap != null && width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;
        _bitmap?.Dispose();
        _skBitmap?.Dispose();

        _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        _skBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    }
}