using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gleam.Engine.Capture;
using Gleam.Engine.Frames;
using Gleam.Engine.Pipeline;
using Gleam.Ui.Imaging;

namespace Gleam.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FramePipeline _pipeline = new();
    private readonly Stopwatch _fpsStopwatch = new();
    private readonly object _imageLock = new();
    private int _frameCounter;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private ICameraCapture? _camera;

    [ObservableProperty]
    private Bitmap? previewImage;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusText = "Idle";

    [ObservableProperty]
    private string fpsText = "FPS: 0";

    [ObservableProperty]
    private string frameInfoText = "Frame: -";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_consumerTask is { IsCompleted: false })
        {
            StatusText = "Already running.";
            return;
        }

        try
        {
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _camera = CameraFactory.CreateDefault();
            _camera.FrameArrived += OnFrameArrived;
            _camera.Start();

            StatusText = "Capturing...";
            _frameCounter = 0;
            _fpsStopwatch.Restart();

            _consumerTask = ConsumeFramesAsync(_cts.Token);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusText = $"Start failed: {ex.Message}";
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        ClearPreview();
        try
        {
            _cts?.Cancel();
            if (_camera != null)
            {
                await Task.Run(() => _camera.Stop());
            }
            if (_camera != null)
            {
                _camera.FrameArrived -= OnFrameArrived;
                _camera.Dispose();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Stop failed: {ex.Message}";
        }
        finally
        {
            _camera = null;
            _cts = null;
            StatusText = "Stopped";
            IsRunning = false;
            ClearPreview();
            ClearDebugInfo();
        }
    }

    private bool CanStart() => !IsRunning;

    private void OnFrameArrived(object? sender, RawFrame frame)
    {
        if (!IsRunning)
        {
            return;
        }
        _pipeline.TryWrite(frame);
    }

    private async Task ConsumeFramesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _pipeline.ReadAllAsync(ct))
            {
                if (!IsRunning)
                {
                    break;
                }
                Bitmap? bitmap = null;
                string? errorText = null;

                try
                {
                    bitmap = FrameToBitmap.Convert(frame);
                    var info = $"Frame: {frame.Width}x{frame.Height} ({frame.PixelFormat})";
                    UpdateFrameInfo(info);
                }
                catch (Exception ex)
                {
                    errorText = ex.Message;
                }

                if (errorText != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusText = $"Conversion error: {errorText}";
                    });
                    continue;
                }

                if (bitmap != null && IsRunning)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => UpdatePreview(bitmap));
                }

                UpdateFps();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = $"Capture error: {ex.Message}";
            });
        }
    }

    private void UpdatePreview(Bitmap bitmap)
    {
        lock (_imageLock)
        {
            PreviewImage?.Dispose();
            PreviewImage = bitmap;
        }
    }

    private void ClearPreview()
    {
        Dispatcher.UIThread.Post(() =>
        {
            lock (_imageLock)
            {
                PreviewImage?.Dispose();
                PreviewImage = null;
            }
        });
    }

    private void ClearDebugInfo()
    {
        Dispatcher.UIThread.Post(() =>
        {
            FpsText = "FPS: 0";
            FrameInfoText = "Frame: -";
        });
    }

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
    }

    private void UpdateFrameInfo(string info)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            FrameInfoText = info;
        }
        else
        {
            Dispatcher.UIThread.Post(() => FrameInfoText = info);
        }
    }

    private void UpdateFps()
    {
        _frameCounter++;
        if (_fpsStopwatch.ElapsedMilliseconds < 500)
        {
            return;
        }

        var fps = _frameCounter / (_fpsStopwatch.ElapsedMilliseconds / 1000.0);
        _frameCounter = 0;
        _fpsStopwatch.Restart();

        Dispatcher.UIThread.Post(() => FpsText = $"FPS: {fps:F1}");
    }
}