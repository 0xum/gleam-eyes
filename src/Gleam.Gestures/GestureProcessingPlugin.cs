using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;
using Gleam.Gestures.Processing;
using Gleam.Gestures.UI;

namespace Gleam.Gestures;

[PluginMetadata("GestureProcessing", "Plugin prepared to preprocess frames for MediaPipe", "1.1.0")]
public class GestureProcessingPlugin : IFrameProcessingPlugin
{
    private static readonly TimeSpan PreviewUpdateInterval = TimeSpan.FromMilliseconds(66);
    private static readonly OverlayStroke LandmarkStroke = new(1.5f, new ColorRgba(80, 255, 160, 180));
    private static readonly OverlayStroke ConnectionStroke = new(2.2f, new ColorRgba(255, 180, 80, 210));
    private static readonly OverlayFill LandmarkFill = new(new ColorRgba(80, 255, 160, 80));
    private static readonly (int A, int B)[] HandConnections =
    [
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16),
        (13, 17), (17, 18), (18, 19), (19, 20),
        (0, 17)
    ];

    private bool _isEnabled = true;
    private bool _isCaptureRunning;
    private int _frameCount;
    private int _preparedFrameCount;
    private int _droppedFrameCount;
    private readonly object _previewSync = new();
    private readonly object _landmarkSync = new();
    private readonly MediaPipePreviewWindowHost _previewWindow = new();
    private readonly MediaPipeGraphRunner _graphRunner = new();
    private readonly SemaphoreSlim _previewSignal = new(0, 1);
    private CancellationTokenSource? _previewCts;
    private Task? _previewLoopTask;
    private RawFrame? _latestRawFrame;
    private GestureLandmarkSnapshot _latestLandmarks = GestureLandmarkSnapshot.Empty;
    private int _landmarkDetectionCount;

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
        _landmarkDetectionCount = 0;
        _isCaptureRunning = true;
        lock (_previewSync)
        {
            _latestRawFrame = null;
        }

        lock (_landmarkSync)
        {
            _latestLandmarks = GestureLandmarkSnapshot.Empty;
        }

        try
        {
            _graphRunner.Start();
        }
        catch (Exception ex)
        {
            PluginLogger.Log($"GestureProcessingPlugin: MediaPipe runner failed to start: {ex}");
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
        DrawLandmarksOverlay(context);

        lock (_previewSync)
        {
            _latestRawFrame = context.Frame;
        }

        if (_previewSignal.CurrentCount == 0)
        {
            _previewSignal.Release();
        }

        PluginLogger.Log(
            $"GestureProcessingPlugin: frame={_frameCount}, prepared={_preparedFrameCount}, dropped={_droppedFrameCount}, detections={_landmarkDetectionCount}, format={context.Frame.PixelFormat}, resolution={context.Frame.Width}x{context.Frame.Height}");
    }

    public void OnEndCapture(PluginEndContext context)
    {
        PluginLogger.Log(
            $"GestureProcessingPlugin: Capture finished. Total={_frameCount}, prepared={_preparedFrameCount}, dropped={_droppedFrameCount}.");

        _isCaptureRunning = false;
        _previewWindow.EndSession();
        _previewCts?.Cancel();
        _graphRunner.Stop();

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
                if (_graphRunner.IsRunning)
                {
                    try
                    {
                        var snapshot = _graphRunner.Process(frame);
                        lock (_landmarkSync)
                        {
                            _latestLandmarks = snapshot;
                        }

                        if (snapshot.HasLandmarks)
                        {
                            _landmarkDetectionCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLogger.Log($"GestureProcessingPlugin: MediaPipe processing failed: {ex.Message}");
                    }
                }

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

    private void DrawLandmarksOverlay(PluginFrameContext context)
    {
        GestureLandmarkSnapshot snapshot;
        lock (_landmarkSync)
        {
            snapshot = _latestLandmarks;
        }

        if (!snapshot.HasLandmarks)
        {
            return;
        }

        var frameWidth = context.Frame.Width;
        var frameHeight = context.Frame.Height;

        foreach (var set in snapshot.Sets)
        {
            foreach (var (a, b) in HandConnections)
            {
                if (a >= set.Points.Count || b >= set.Points.Count)
                {
                    continue;
                }

                var start = set.Points[a];
                var end = set.Points[b];
                context.Scene.Add(new OverlayLine(
                    ToOverlayPoint(start, frameWidth, frameHeight),
                    ToOverlayPoint(end, frameWidth, frameHeight),
                    ConnectionStroke));
            }

            foreach (var point in set.Points)
            {
                context.Scene.Add(new OverlayCircle(
                    ToOverlayPoint(point, frameWidth, frameHeight),
                    4f,
                    LandmarkStroke,
                    LandmarkFill));
            }
        }
    }

    private static OverlayPoint ToOverlayPoint(GestureLandmarkPoint point, int frameWidth, int frameHeight)
    {
        var x = Math.Clamp(point.X, 0f, 1f) * frameWidth;
        var y = Math.Clamp(point.Y, 0f, 1f) * frameHeight;
        return OverlayPoint.FromPixels(x, y);
    }

}