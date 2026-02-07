using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
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
using Gleam.Ui.ViewModels.SidebarTabs;

namespace Gleam.Ui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string GesturePluginTypeName = "Gleam.Gestures.GestureProcessingPlugin";

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
    private long _pluginTickCounter;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private ICameraCapture? _camera;
    private readonly ObservableCollection<string> _pluginLogs = new();
    private readonly ObservableCollection<SidebarTabViewModel> _sidebarTabs = new();
    private object? _gesturePluginInstance;
    private MethodInfo? _gestureOpenDebugMethod;
    private PropertyInfo? _gestureCanOpenDebugProperty;
    private PropertyInfo? _gestureToolbarLabelProperty;
    private EventInfo? _gestureToolbarStateChangedEvent;
    private Delegate? _gestureToolbarStateChangedHandler;

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
    private string pluginTickText = "Ticks: 0";

    [ObservableProperty]
    private bool isGridEnabled = true;

    [ObservableProperty]
    private bool isAnimatedEnabled;

    [ObservableProperty]
    private bool isDebugMode;

    [ObservableProperty]
    private SidebarTabViewModel? selectedSidebarTab;

    [ObservableProperty]
    private bool isGestureDebugButtonVisible;

    [ObservableProperty]
    private string gestureDebugButtonLabel = "Open Gesture Debug";

    public ReadOnlyObservableCollection<PluginDescriptor> Plugins { get; }
    public ReadOnlyObservableCollection<string> PluginLogs { get; }
    public ReadOnlyObservableCollection<SidebarTabViewModel> SidebarTabs { get; }

    public MainWindowViewModel()
    {
        Plugins = new ReadOnlyObservableCollection<PluginDescriptor>(_plugins);
        PluginLogs = new ReadOnlyObservableCollection<string>(_pluginLogs);
        SidebarTabs = new ReadOnlyObservableCollection<SidebarTabViewModel>(_sidebarTabs);
        _pluginHandler.LoadPlugins();
        RegisterBuiltIn(new OverlayModulePluginAdapter(_gridModule), _gridModule.Name, "Overlay grid", "1.0.0");
        RegisterBuiltIn(new OverlayModulePluginAdapter(_animatedModule), _animatedModule.Name, "Animated overlay", "1.0.0");
        RefreshPlugins();
        RefreshGestureDebugIntegration();
        RefreshSidebarTabs();
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

        RefreshGestureDebugIntegration();
    }

    private void RefreshGestureDebugIntegration()
    {
        UnsubscribeGestureToolbarEvent();

        var descriptor = _pluginHandler.Plugins
            .FirstOrDefault(p => string.Equals(p.Instance.GetType().FullName, GesturePluginTypeName, StringComparison.Ordinal));

        if (descriptor == null)
        {
            _gesturePluginInstance = null;
            _gestureOpenDebugMethod = null;
            _gestureCanOpenDebugProperty = null;
            _gestureToolbarLabelProperty = null;
            IsGestureDebugButtonVisible = false;
            GestureDebugButtonLabel = "Open Gesture Debug";
            return;
        }

        var plugin = descriptor.Instance;
        var pluginType = plugin.GetType();

        _gesturePluginInstance = plugin;
        _gestureOpenDebugMethod = pluginType.GetMethod("OpenDebugWindow", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        _gestureCanOpenDebugProperty = pluginType.GetProperty("CanOpenDebugWindow", BindingFlags.Public | BindingFlags.Instance);
        _gestureToolbarLabelProperty = pluginType.GetProperty("ToolbarActionLabel", BindingFlags.Public | BindingFlags.Instance);
        _gestureToolbarStateChangedEvent = pluginType.GetEvent("ToolbarStateChanged", BindingFlags.Public | BindingFlags.Instance);

        SubscribeGestureToolbarEvent();
        UpdateGestureDebugButtonState();
    }

    private void SubscribeGestureToolbarEvent()
    {
        if (_gesturePluginInstance == null || _gestureToolbarStateChangedEvent == null)
        {
            return;
        }

        var handler = new Action(UpdateGestureDebugButtonState);
        _gestureToolbarStateChangedEvent.AddEventHandler(_gesturePluginInstance, handler);
        _gestureToolbarStateChangedHandler = handler;
    }

    private void UnsubscribeGestureToolbarEvent()
    {
        if (_gesturePluginInstance == null || _gestureToolbarStateChangedEvent == null || _gestureToolbarStateChangedHandler == null)
        {
            return;
        }

        _gestureToolbarStateChangedEvent.RemoveEventHandler(_gesturePluginInstance, _gestureToolbarStateChangedHandler);
        _gestureToolbarStateChangedHandler = null;
    }

    private void UpdateGestureDebugButtonState()
    {
        var canOpen = false;
        if (_gesturePluginInstance != null && _gestureCanOpenDebugProperty != null)
        {
            canOpen = _gestureCanOpenDebugProperty.GetValue(_gesturePluginInstance) as bool? == true;
        }

        var label = "Open Gesture Debug";
        if (_gesturePluginInstance != null && _gestureToolbarLabelProperty != null)
        {
            label = _gestureToolbarLabelProperty.GetValue(_gesturePluginInstance) as string ?? label;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            IsGestureDebugButtonVisible = canOpen;
            GestureDebugButtonLabel = label;
            OpenGestureDebugWindowCommand.NotifyCanExecuteChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsGestureDebugButtonVisible = canOpen;
                GestureDebugButtonLabel = label;
                OpenGestureDebugWindowCommand.NotifyCanExecuteChanged();
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenGestureDebugWindow))]
    private void OpenGestureDebugWindow()
    {
        try
        {
            _gestureOpenDebugMethod?.Invoke(_gesturePluginInstance, null);
        }
        catch (Exception ex)
        {
            PluginLogger.Log($"[UI] Failed to open gesture plugin debug window: {ex.Message}");
        }
    }

    private bool CanOpenGestureDebugWindow() => IsGestureDebugButtonVisible;

    private void RefreshSidebarTabs()
    {
        var selectedTitle = SelectedSidebarTab?.Title;

        _sidebarTabs.Clear();
        _sidebarTabs.Add(new DebugSidebarTabViewModel());
        _sidebarTabs.Add(new PluginsSidebarTabViewModel());
        _sidebarTabs.Add(new LogsSidebarTabViewModel());
        _sidebarTabs.Add(new NativeSettingsSidebarTabViewModel());

        foreach (var descriptor in _pluginHandler.Plugins)
        {
            var plugin = descriptor.Instance;
            var pluginType = plugin.GetType();
            var createSettingsMethod = pluginType.GetMethod("CreateSettingsView", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (createSettingsMethod == null)
            {
                continue;
            }

            try
            {
                var settingsView = createSettingsMethod.Invoke(plugin, null);
                if (settingsView is not Control control)
                {
                    continue;
                }

                var tabTitle = pluginType.GetProperty("SettingsTabTitle", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(plugin) as string;

                tabTitle = string.IsNullOrWhiteSpace(tabTitle)
                    ? descriptor.Metadata.Name
                    : tabTitle;

                _sidebarTabs.Add(new PluginSettingsSidebarTabViewModel(tabTitle, descriptor.Metadata.Name, control));
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"[UI] Failed to create settings tab for plugin '{descriptor.Metadata.Name}': {ex.Message}");
            }
        }

        SelectedSidebarTab = _sidebarTabs.FirstOrDefault(t => t.Title == selectedTitle)
                           ?? _sidebarTabs.FirstOrDefault();
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
            PluginLogger.Log("[UI] StartAsync started.");
            _cts = new CancellationTokenSource();
            _camera = CameraFactory.CreateDefault();
            _camera.FrameArrived += OnFrameArrived;
            _camera.Start();

            StatusText = "Capturing...";
            _frameCounter = 0;
            _pluginTickCounter = 0;
            PluginTickText = "Ticks: 0";
            _fpsStopwatch.Restart();

            _consumerTask = ConsumeFramesAsync(_cts.Token);
            UpdateGestureDebugButtonState();
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
            UpdateGestureDebugButtonState();
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
                    UpdatePluginTicks();
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
            PluginTickText = "Ticks: 0";
        });
    }

    private void UpdatePluginTicks()
    {
        _pluginTickCounter++;
        var text = $"Ticks: {_pluginTickCounter}";

        if (Dispatcher.UIThread.CheckAccess())
        {
            PluginTickText = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() => PluginTickText = text);
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StartCommand.NotifyCanExecuteChanged();
            OpenGestureDebugWindowCommand.NotifyCanExecuteChanged();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            StartCommand.NotifyCanExecuteChanged();
            OpenGestureDebugWindowCommand.NotifyCanExecuteChanged();
        });
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