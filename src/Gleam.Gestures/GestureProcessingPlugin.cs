using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Gleam.Engine.Frames;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;
using Gleam.Gestures.Processing;
using Gleam.Gestures.UI;

namespace Gleam.Gestures;

[PluginMetadata("GestureProcessing", "Plugin prepared to preprocess frames for MediaPipe", "1.1.0")]
public class GestureProcessingPlugin : IFrameProcessingPlugin
{
    private static readonly TimeSpan PreviewUpdateInterval = TimeSpan.FromMilliseconds(66);

    private bool _isEnabled = true;
    private bool _isCaptureRunning;
    private int _frameCount;
    private int _preparedFrameCount;
    private int _droppedFrameCount;
    private readonly object _previewSync = new();
    private readonly MediaPipePreviewWindowHost _previewWindow = new();
    private readonly SemaphoreSlim _previewSignal = new(0, 1);
    private CancellationTokenSource? _previewCts;
    private Task? _previewLoopTask;
    private RawFrame? _latestRawFrame;

    public event Action? ToolbarStateChanged;

    public GestureProcessingPlugin()
    {
        _previewWindow.StateChanged += RaiseToolbarStateChanged;
    }

    public string ToolbarActionLabel => "Open Gesture Debug";

    public bool CanOpenDebugWindow => _isCaptureRunning && IsEnabled && !_previewWindow.IsWindowOpen;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            RaiseToolbarStateChanged();
        }
    }

    public void OpenDebugWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.OpenOnDemand();
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: failed to open debug window on demand: {ex.Message}");
            }
        });

        RaiseToolbarStateChanged();
    }

    public void OnStartCapture(PluginStartContext context)
    {
        PluginLogger.Log("GestureProcessingPlugin: Capture started (MediaPipe preprocessing).");
        _frameCount = 0;
        _preparedFrameCount = 0;
        _droppedFrameCount = 0;
        _isCaptureRunning = true;
        lock (_previewSync)
        {
            _latestRawFrame = null;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        _previewLoopTask = Task.Run(() => PreviewLoopAsync(_previewCts.Token));
        _previewWindow.StartSession();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.Open();
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: failed to open preview window: {ex.Message}");
            }
        });

        RaiseToolbarStateChanged();
    }

    public void OnUpdateCapture(PluginFrameContext context)
    {
        _frameCount++;
        lock (_previewSync)
        {
            _latestRawFrame = context.Frame;
        }

        if (_previewSignal.CurrentCount == 0)
        {
            _previewSignal.Release();
        }

        // Exibe log a cada 100 frames para evitar sobrecarregar o log
        if (_frameCount % 100 == 0)
        {
            PluginLogger.Log(
                $"GestureProcessingPlugin: frame={_frameCount}, prepared={_preparedFrameCount}, dropped={_droppedFrameCount}, format={context.Frame.PixelFormat}, resolution={context.Frame.Width}x{context.Frame.Height}");
        }
    }

    public void OnEndCapture(PluginEndContext context)
    {
        PluginLogger.Log(
            $"GestureProcessingPlugin: Capture finished. Total={_frameCount}, prepared={_preparedFrameCount}, dropped={_droppedFrameCount}.");

        _isCaptureRunning = false;
        _previewWindow.EndSession();
        _previewCts?.Cancel();

        _previewLoopTask = null;
        _previewCts?.Dispose();
        _previewCts = null;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _previewWindow.Close();
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"GestureProcessingPlugin: failed to close preview window: {ex.Message}");
            }
        });

        RaiseToolbarStateChanged();
    }

    private async Task PreviewLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _previewSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            PreparedFrame? frame;
            RawFrame? rawFrame;
            lock (_previewSync)
            {
                rawFrame = _latestRawFrame;
                _latestRawFrame = null;
            }

            frame = rawFrame == null ? null : MediaPipeFramePreprocessor.Prepare(rawFrame);

            if (rawFrame != null && frame == null)
            {
                _droppedFrameCount++;
            }

            if (frame != null)
            {
                _preparedFrameCount++;
            }

            if (frame != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        _previewWindow.UpdateFrame(frame);
                    }
                    catch (Exception ex)
                    {
                        PluginLogger.Log($"GestureProcessingPlugin: failed to update preview window: {ex.Message}");
                    }
                }, DispatcherPriority.Background);
            }

            try
            {
                await Task.Delay(PreviewUpdateInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void RaiseToolbarStateChanged()
    {
        ToolbarStateChanged?.Invoke();
    }

}