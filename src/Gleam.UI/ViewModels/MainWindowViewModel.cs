using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gleam.Engine.Capture;
using Gleam.Engine.Frames;
using Gleam.Engine.Modules;
using Gleam.Engine.Overlays;
using Gleam.Engine.Processing;
using Gleam.Engine.Pipeline;
using Gleam.Engine.Plugins;
using Gleam.Ui.Imaging;

namespace Gleam.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FramePipeline _pipeline = new();
    private readonly Stopwatch _fpsStopwatch = new();
    private readonly object _imageLock = new();
    private readonly PluginHandler _pluginHandler = new();
    private readonly ObservableCollection<PluginDescriptor> _plugins = new();
    private readonly BasicGridOverlayModule _gridModule = new();
    private readonly AnimatedShapesOverlayModule _animatedModule = new();
    private readonly OverlayBitmapComposer _composer = new();
    private readonly SampleGesturePlugin _sampleGesture = new();
    private int _frameCounter;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private ICameraCapture? _camera;
    private readonly ObservableCollection<string> _pluginLogs = new();

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

    [ObservableProperty]
    private bool isGridEnabled = true;

    [ObservableProperty]
    private bool isAnimatedEnabled;

    public ReadOnlyObservableCollection<PluginDescriptor> Plugins { get; }
    public ReadOnlyObservableCollection<string> PluginLogs { get; }

    public MainWindowViewModel()
    {
        Plugins = new ReadOnlyObservableCollection<PluginDescriptor>(_plugins);
        PluginLogs = new ReadOnlyObservableCollection<string>(_pluginLogs);
        _pluginHandler.LoadPlugins();
        RegisterBuiltIn(new OverlayModulePluginAdapter(_gridModule), _gridModule.Name, "Overlay grid", "1.0.0");
        RegisterBuiltIn(new OverlayModulePluginAdapter(_animatedModule), _animatedModule.Name, "Overlay animado", "1.0.0");
        RefreshPlugins();
        PluginLogger.Message += OnPluginLog;
        PluginLogger.Log("[UI] Plugin logger subscribed.");
    }

    private void OnPluginLog(string message)
    {
        void Append()
        {
            if (_pluginLogs.Count > 200)
            {
                _pluginLogs.RemoveAt(0);
            }
            _pluginLogs.Add(message);
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Append();
        }
        else
        {
            Dispatcher.UIThread.Post(Append);
        }
    }

    private void RegisterBuiltIn(IFrameProcessingPlugin plugin, string name, string description, string version)
    {
        if (plugin is not null)
        {
            _pluginHandler.RegisterPlugin(plugin, new PluginMetadataAttribute(name, description, version), "built-in");
        }
    }

    private void RefreshPlugins()
    {
        _plugins.Clear();
        foreach (var descriptor in _pluginHandler.Plugins)
        {
            _plugins.Add(descriptor);
        }
    }

    partial void OnIsGridEnabledChanged(bool value)
    {
        _gridModule.IsEnabled = value;
    }

    partial void OnIsAnimatedEnabledChanged(bool value)
    {
        _animatedModule.IsEnabled = value;
    }

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
            _composer.Reset();
            _pluginHandler.OnStartCapture();
            PluginLogger.Log("[UI] StartAsync iniciado.");
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

            if (_consumerTask is { IsCompleted: false })
            {
                await _consumerTask.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = $"Stop failed: {ex.Message}";
            });
        }
        finally
        {
            _camera = null;
            _cts = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = "Stopped";
                IsRunning = false;
                ClearPreview();
                ClearDebugInfo();
                _composer.Reset();
            });
            _pluginHandler.OnEndCapture();
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
                WriteableBitmap? bitmap = null;
                string? errorText = null;

                try
                {
                    var processingResult = _pluginHandler.OnUpdateCapture(frame);
                    bitmap = _composer.Compose(frame, processingResult.Scene);
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

    private void UpdatePreview(WriteableBitmap bitmap)
    {
        Bitmap? previous;
        lock (_imageLock)
        {
            if (ReferenceEquals(PreviewImage, bitmap))
            {
                OnPropertyChanged(nameof(PreviewImage));
                return;
            }

            previous = PreviewImage;
            PreviewImage = bitmap;
        }

        if (previous != null)
        {
            Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);
        }
    }

    private void ClearPreview()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Bitmap? previous;
            lock (_imageLock)
            {
                previous = PreviewImage;
                PreviewImage = null;
            }

            if (previous != null)
            {
                Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            StartCommand.NotifyCanExecuteChanged();
            return;
        }

        Dispatcher.UIThread.Post(() => StartCommand.NotifyCanExecuteChanged());
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