using System.Runtime.InteropServices;
using System.Buffers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gleam.Engine.Frames;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;

namespace Gleam.Gestures.UI;

internal sealed class MediaPipePreviewWindowHost
{
    private Window? _window;
    private Image? _image;
    private WriteableBitmap? _bitmap;
    private byte[]? _scratchBgra;
    private int _width;
    private int _height;
    private bool _sessionActive;

    public event Action? StateChanged;

    public bool IsWindowOpen => _window?.IsVisible == true;

    public bool IsSessionActive => _sessionActive;

    public void StartSession()
    {
        _sessionActive = true;
        StateChanged?.Invoke();
    }

    public void EndSession()
    {
        _sessionActive = false;
        StateChanged?.Invoke();
    }

    public void Open()
    {
        if (!_sessionActive)
        {
            return;
        }

        if (_window != null)
        {
            if (!_window.IsVisible)
            {
                _window.Show();
                StateChanged?.Invoke();
            }

            return;
        }

        _image = new Image { Stretch = Avalonia.Media.Stretch.Uniform };
        _window = new Window
        {
            Title = "MediaPipe - Frame de Entrada",
            Width = 720,
            Height = 540,
            Content = _image
        };

        _window.Closed += (_, _) =>
        {
            _bitmap?.Dispose();
            _bitmap = null;
            ReturnScratchBuffer();
            _width = 0;
            _height = 0;
            _image = null;
            _window = null;
            StateChanged?.Invoke();
        };

        _window.Show();
        StateChanged?.Invoke();
    }

    public void OpenOnDemand()
    {
        Open();
    }

    public void UpdateFrame(PreparedFrame frame)
    {
        if (!_sessionActive)
        {
            return;
        }

        if (_window == null || _image == null)
        {
            return;
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        try
        {
            EnsureBitmap(frame.Width, frame.Height);
            if (_bitmap == null || _scratchBgra == null)
            {
                return;
            }

            ConvertRgb24ToBgra32(frame.Data, frame.Width, frame.Height, _scratchBgra);
            using (var locked = _bitmap.Lock())
            {
                unsafe
                {
                    var dstStride = locked.RowBytes;
                    var srcStride = frame.Width * 4;
                    var dstAddr = (byte*)locked.Address;
                    fixed (byte* srcAddr = _scratchBgra)
                    {
                        for (var y = 0; y < frame.Height; y++)
                        {
                            Buffer.MemoryCopy(
                                srcAddr + (y * srcStride),
                                dstAddr + (y * dstStride),
                                (ulong)dstStride,
                                (ulong)srcStride);
                        }
                    }
                }
            }

            if (!ReferenceEquals(_image.Source, _bitmap))
            {
                _image.Source = _bitmap;
            }

            _window.Title = $"MediaPipe INPUT - {frame.Width}x{frame.Height} [{frame.PixelFormat}] ts={frame.TimestampNs / 1_000_000.0:F1}ms";
        }
        catch (Exception)
        {
            // Ignore UI update errors
        }
    }

    public void UpdateFrame(RawFrame frame)
    {
        if (!_sessionActive)
        {
            return;
        }

        if (_window == null || _image == null)
        {
            return;
        }

        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        try
        {
            EnsureBitmap(frame.Width, frame.Height);
            if (_bitmap == null || _scratchBgra == null)
            {
                return;
            }

            var pixelFormat = frame.PixelFormat;
            if (string.Equals(pixelFormat, "RGB24", StringComparison.OrdinalIgnoreCase))
            {
                ConvertRgb24ToBgra32(frame.Data, frame.Width, frame.Height, _scratchBgra);
            }
            else if (string.Equals(pixelFormat, "BGRA32", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(pixelFormat, "ARGB32", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(pixelFormat, "RGB32", StringComparison.OrdinalIgnoreCase))
            {
                CopyBgraLikeToBgra32(frame.Data, frame.Width, frame.Height, _scratchBgra);
            }
            else
            {
                return;
            }

            using (var locked = _bitmap.Lock())
            {
                unsafe
                {
                    var dstStride = locked.RowBytes;
                    var srcStride = frame.Width * 4;
                    var dstAddr = (byte*)locked.Address;
                    fixed (byte* srcAddr = _scratchBgra)
                    {
                        for (var y = 0; y < frame.Height; y++)
                        {
                            Buffer.MemoryCopy(
                                srcAddr + (y * srcStride),
                                dstAddr + (y * dstStride),
                                (ulong)dstStride,
                                (ulong)srcStride);
                        }
                    }
                }
            }

            if (!ReferenceEquals(_image.Source, _bitmap))
            {
                _image.Source = _bitmap;
            }

            _window.Title = $"MediaPipe INPUT - {frame.Width}x{frame.Height} [{frame.PixelFormat}] ts={frame.TimestampNs / 1_000_000.0:F1}ms";
        }
        catch (Exception)
        {
            // Ignore UI update errors
        }
    }

    public void Close()
    {
        if (_window == null)
        {
            _bitmap?.Dispose();
            _bitmap = null;
            ReturnScratchBuffer();
            _width = 0;
            _height = 0;
            return;
        }

        _window.Close();
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap != null && _width == width && _height == height)
        {
            return;
        }

        _bitmap?.Dispose();
        ReturnScratchBuffer();
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        _scratchBgra = ArrayPool<byte>.Shared.Rent(width * height * 4);

        _width = width;
        _height = height;
    }

    private void ReturnScratchBuffer()
    {
        if (_scratchBgra == null)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_scratchBgra);
        _scratchBgra = null;
    }

    private static void ConvertRgb24ToBgra32(byte[] source, int width, int height, byte[] destination)
    {
        var pixelCount = width * height;
        var srcHandle = GCHandle.Alloc(source, GCHandleType.Pinned);
        var dstHandle = GCHandle.Alloc(destination, GCHandleType.Pinned);

        try
        {
            unsafe
            {
                byte* srcPtr = (byte*)srcHandle.AddrOfPinnedObject();
                byte* dstPtr = (byte*)dstHandle.AddrOfPinnedObject();

                for (var i = 0; i < pixelCount; i++)
                {
                    var srcIdx = i * 3;
                    var dstIdx = i << 2;
                    dstPtr[dstIdx] = srcPtr[srcIdx + 2];     // B
                    dstPtr[dstIdx + 1] = srcPtr[srcIdx + 1]; // G
                    dstPtr[dstIdx + 2] = srcPtr[srcIdx];     // R
                    dstPtr[dstIdx + 3] = byte.MaxValue;      // A
                }
            }
        }
        finally
        {
            srcHandle.Free();
            dstHandle.Free();
        }
    }

    private static void CopyBgraLikeToBgra32(byte[] source, int width, int height, byte[] destination)
    {
        var byteCount = Math.Min(source.Length, width * height * 4);
        Buffer.BlockCopy(source, 0, destination, 0, byteCount);
    }
}
