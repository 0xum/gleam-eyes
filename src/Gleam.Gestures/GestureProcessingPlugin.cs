using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Gleam.Engine.Processing;
using SkiaSharp;

namespace Gleam.Gestures;

[PluginMetadata("GestureProcessing", "Plugin preparado para pré-processar frames para MediaPipe", "1.1.0")]
public class GestureProcessingPlugin : IFrameProcessingPlugin
{
    private int _frameCount;
    private int _preparedFrameCount;
    private int _droppedFrameCount;
    private PreparedFrame? _lastPreparedFrame;
    private readonly MediaPipePreviewWindowHost _previewWindow = new();

    public bool IsEnabled { get; set; } = true;

    public void OnStartCapture(PluginStartContext context)
    {
        PluginLogger.Log("GestureProcessingPlugin: Captura iniciada (pré-processamento para MediaPipe).");
        _frameCount = 0;
        _preparedFrameCount = 0;
        _droppedFrameCount = 0;
        _lastPreparedFrame = null;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.Open();
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: falha ao abrir janela de preview: {ex.Message}");
            }
        });
    }

    public void OnUpdateCapture(PluginFrameContext context)
    {
        _frameCount++;
        var preparedFrame = PrepareFrameForMediaPipe(context);

        if (preparedFrame is null)
        {
            _droppedFrameCount++;
        }
        else
        {
            _preparedFrameCount++;
            _lastPreparedFrame = preparedFrame;
            DispatchPreviewUpdate(preparedFrame);
        }

        // Exibe log a cada 100 frames para evitar sobrecarregar o log
        if (_frameCount % 100 == 0)
        {
            PluginLogger.Log(
                $"GestureProcessingPlugin: frame={_frameCount}, preparados={_preparedFrameCount}, descartados={_droppedFrameCount}, formato={context.Frame.PixelFormat}, resolução={context.Frame.Width}x{context.Frame.Height}");
        }
    }

    public void OnEndCapture(PluginEndContext context)
    {
        PluginLogger.Log(
            $"GestureProcessingPlugin: Captura finalizada. Total={_frameCount}, preparados={_preparedFrameCount}, descartados={_droppedFrameCount}.");

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.Close();
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: falha ao fechar janela de preview: {ex.Message}");
            }
        });
    }

    private void DispatchPreviewUpdate(PreparedFrame frame)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.UpdateFrame(frame);
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: falha ao atualizar janela de preview: {ex.Message}");
            }
        }, DispatcherPriority.Render);
    }

    private static PreparedFrame? PrepareFrameForMediaPipe(PluginFrameContext context)
    {
        var frame = context.Frame;
        var pixelFormat = frame.PixelFormat;

        if (string.Equals(pixelFormat, "RGB24", StringComparison.OrdinalIgnoreCase))
        {
            return new PreparedFrame(
                frame.Width,
                frame.Height,
                "RGB24",
                frame.TimestampNs,
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
                frame.TimestampNs,
                converted);
        }

        if (TryDecodeEncodedToRgb24(frame.Data, out var decodedWidth, out var decodedHeight, out var decodedRgb))
        {
            return new PreparedFrame(
                decodedWidth,
                decodedHeight,
                "RGB24",
                frame.TimestampNs,
                decodedRgb);
        }

        if (context.Frame.TimestampNs % 300 == 0)
        {
            PluginLogger.Log(
                $"GestureProcessingPlugin: formato '{pixelFormat}' ainda não suportado no pré-processamento para MediaPipe.");
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
                $"Frame inválido: esperado ao menos {expectedSourceLength} bytes para formato de 32bpp, recebido {source.Length}.");
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

    private sealed record PreparedFrame(
        int Width,
        int Height,
        string PixelFormat,
        long TimestampNs,
        byte[] Data);

    private sealed class MediaPipePreviewWindowHost
    {
        private Window? _window;
        private Image? _image;
        private WriteableBitmap? _bitmap;
        private int _width;
        private int _height;

        public void Open()
        {
            if (_window != null)
            {
                if (!_window.IsVisible)
                {
                    _window.Show();
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
                _width = 0;
                _height = 0;
                _image = null;
                _window = null;
            };

            _window.Show();
        }

        public void UpdateFrame(PreparedFrame frame)
        {
            Open();

            if (_window == null || _image == null)
            {
                return;
            }

            EnsureBitmap(frame.Width, frame.Height);
            if (_bitmap == null)
            {
                return;
            }

            var bgra = ConvertRgb24ToBgra32(frame.Data, frame.Width, frame.Height);
            using (var locked = _bitmap.Lock())
            {
                var sourceStride = frame.Width * 4;
                for (var y = 0; y < frame.Height; y++)
                {
                    var sourceOffset = y * sourceStride;
                    var destinationPtr = IntPtr.Add(locked.Address, y * locked.RowBytes);
                    Marshal.Copy(bgra, sourceOffset, destinationPtr, sourceStride);
                }
            }

            // força um refresh visual confiável do controle
            _image.Source = null;
            _image.Source = _bitmap;
            _image.InvalidateVisual();
            _window.Title = $"MediaPipe - {frame.Width}x{frame.Height} [{frame.PixelFormat}]";
        }

        public void Close()
        {
            if (_window == null)
            {
                _bitmap?.Dispose();
                _bitmap = null;
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
            _bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            _width = width;
            _height = height;
        }

        private static byte[] ConvertRgb24ToBgra32(byte[] source, int width, int height)
        {
            var pixelCount = width * height;
            var expectedLength = pixelCount * 3;
            if (source.Length < expectedLength)
            {
                throw new InvalidOperationException(
                    $"Frame RGB24 inválido: esperado ao menos {expectedLength} bytes, recebido {source.Length}.");
            }

            var destination = new byte[pixelCount * 4];
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

            return destination;
        }
    }
}