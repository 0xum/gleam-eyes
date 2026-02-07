using System.Runtime.InteropServices;
using System.Buffers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

        _image.InvalidateVisual();
        _window.Title = $"MediaPipe - {frame.Width}x{frame.Height} [{frame.PixelFormat}]";
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
        var expectedLength = pixelCount * 3;
        if (source.Length < expectedLength)
        {
            throw new InvalidOperationException(
                $"Invalid RGB24 frame: expected at least {expectedLength} bytes, got {source.Length}.");
        }

        var destinationLength = pixelCount * 4;
        if (destination.Length < destinationLength)
        {
            throw new InvalidOperationException(
                $"Invalid destination buffer: expected at least {destinationLength} bytes, got {destination.Length}.");
        }

        var sourceIndex = 0;
        var destinationIndex = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var r = source[sourceIndex];
            var g = source[sourceIndex + 1];
            var b = source[sourceIndex + 2];

            destination[destinationIndex] = b;
            destination[destinationIndex + 1] = g;
            destination[destinationIndex + 2] = r;
            destination[destinationIndex + 3] = byte.MaxValue;

            sourceIndex += 3;
            destinationIndex += 4;
        }
    }
}
