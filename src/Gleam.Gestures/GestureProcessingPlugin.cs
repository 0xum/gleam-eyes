using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
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
    private const int MaxLandmarkAgFrames = 15;
    private const int EmptySnapshotClearThresholdFrames = 3;
    private static readonly long PreviewUpdateIntervalTicks = (long)(PreviewUpdateInterval.TotalSeconds * Stopwatch.Frequency);
    private const int RuntimeLogIntervalFrames = 60;
    private const float NearHandsDistanceRatio = 0.18f;
    private const float MidHandsDistanceRatio = 0.35f;
    private const float PalmOpenMinRatio = 1.05f;
    private const float PalmOpenMaxRatio = 1.95f;
    private static readonly OverlayStroke LandmarkStroke = new(1.5f, new ColorRgba(80, 255, 160, 180));
    private static readonly OverlayStroke ConnectionStroke = new(2.2f, new ColorRgba(255, 180, 80, 210));
    private static readonly OverlayStroke HandsDistanceNearStroke = new(4f, new ColorRgba(80, 255, 120, 230));
    private static readonly OverlayStroke HandsDistanceMidStroke = new(4f, new ColorRgba(255, 220, 80, 230));
    private static readonly OverlayStroke HandsDistanceFarStroke = new(4f, new ColorRgba(255, 80, 80, 230));
    private static readonly OverlayStroke PalmOpenCircleStroke = new(3f, new ColorRgba(90, 200, 255, 220));
    private static readonly OverlayFill PalmOpenCircleFill = new(new ColorRgba(90, 200, 255, 48));
    private static readonly OverlayFill LandmarkFill = new(new ColorRgba(80, 255, 160, 80));
    private static readonly int[] FingerTipIndices = [4, 8, 12, 16, 20];
    private static readonly int[] PalmCenterIndices = [0, 5, 9, 13, 17];
    private static readonly int[] PalmBaseIndices = [5, 9, 13, 17];
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
    private readonly MediaPipePreviewWindowHost _previewWindow = new();
    private readonly MediaPipeGraphRunner _graphRunner = new();
    private readonly SemaphoreSlim _previewSignal = new(0, 1);
    private CancellationTokenSource? _previewCts;
    private Task? _previewLoopTask;
    private RawFrame? _latestRawFrame;
    private GestureLandmarkSnapshot _latestLandmarks = GestureLandmarkSnapshot.Empty;
    private long _nextPreviewRenderTick;
    private int _landmarkDetectionCount;
    private int _lastDetectedHands;
    private int _lastDetectedPoints;
    private bool _mediaPipeFaulted;
    private long _lastInferenceElapsedTicks;
    private long _avgInferenceElapsedTicks;
    private long _lastOverlayAgeUs;
    private int _framesSinceLastLandmarkUpdate;
    private int _consecutiveEmptySnapshots;

    public string RuntimePerfLabel
    {
        get
        {
            var lastMs = TicksToMs(Volatile.Read(ref _lastInferenceElapsedTicks));
            var avgMs = TicksToMs(Volatile.Read(ref _avgInferenceElapsedTicks));
            var overlayAgeFrames = Volatile.Read(ref _lastOverlayAgeUs);
            var hands = Volatile.Read(ref _lastDetectedHands);
            var points = Volatile.Read(ref _lastDetectedPoints);
            return $"Infer={lastMs:F1}ms(avg {avgMs:F1}) age={overlayAgeFrames}f prep={_preparedFrameCount} drop={_droppedFrameCount} detFrames={_landmarkDetectionCount} hands={hands} points={points}";
        }
    }

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
        _lastDetectedHands = 0;
        _lastDetectedPoints = 0;
        _mediaPipeFaulted = false;
        _lastOverlayAgeUs = 0;
        _nextPreviewRenderTick = 0;
        _consecutiveEmptySnapshots = 0;
        _isCaptureRunning = true;
        Interlocked.Exchange(ref _latestRawFrame, null);
        Volatile.Write(ref _latestLandmarks, GestureLandmarkSnapshot.Empty);

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

        Interlocked.Exchange(ref _latestRawFrame, context.Frame);
        
        if (_previewSignal.CurrentCount == 0)
        {
            _previewSignal.Release();
        }

        if (_frameCount % RuntimeLogIntervalFrames == 0)
        {
            PluginLogger.Log(
                $"GestureProcessingPlugin: frame={_frameCount}, prepared={_preparedFrameCount}, dropped={_droppedFrameCount}, detections={_landmarkDetectionCount}, format={context.Frame.PixelFormat}, resolution={context.Frame.Width}x{context.Frame.Height}");
        }
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
            var rawFrame = Interlocked.Exchange(ref _latestRawFrame, null);
            if (rawFrame == null)
            {
                continue;
            }

            frame = MediaPipeFramePreprocessor.Prepare(rawFrame);

            if (frame == null)
            {
                _droppedFrameCount++;
                if (_frameCount % 60 == 0)
                {
                    PluginLogger.Log($"GestureProcessingPlugin: frame preparation failed for format {rawFrame.PixelFormat}");
                }
            }
            else
            {
                _preparedFrameCount++;
            }

            if (frame != null)
            {
                if (_graphRunner.IsRunning && !_mediaPipeFaulted)
                {
                    try
                    {
                        var inferenceStart = Stopwatch.GetTimestamp();
                        var snapshot = _graphRunner.Process(frame);
                        var inferenceElapsed = Stopwatch.GetTimestamp() - inferenceStart;

                        Interlocked.Exchange(ref _lastInferenceElapsedTicks, inferenceElapsed);
                        var previousAvg = Volatile.Read(ref _avgInferenceElapsedTicks);
                        var nextAvg = previousAvg == 0
                            ? inferenceElapsed
                            : ((previousAvg * 7) + inferenceElapsed) / 8;
                        Interlocked.Exchange(ref _avgInferenceElapsedTicks, nextAvg);

                        if (_frameCount % 60 == 0)
                        {
                            var lastMs = TicksToMs(inferenceElapsed);
                            PluginLogger.Log($"[Perf] MediaPipe Process: {lastMs:F2}ms");
                        }

                        // MediaPipeGraphRunner retorna exatamente GestureLandmarkSnapshot.Empty
                        // quando não há pacote novo disponível neste ciclo.
                        // Nessa situação mantemos o último estado para evitar flicker.
                        if (!ReferenceEquals(snapshot, GestureLandmarkSnapshot.Empty))
                        {
                            if (snapshot.HasLandmarks)
                            {
                                Volatile.Write(ref _latestLandmarks, snapshot);
                                Volatile.Write(ref _framesSinceLastLandmarkUpdate, 0);
                                Volatile.Write(ref _consecutiveEmptySnapshots, 0);
                                _landmarkDetectionCount++;

                                var hands = snapshot.Sets.Count;
                                var points = 0;
                                for (var i = 0; i < hands; i++)
                                {
                                    points += snapshot.Sets[i].Points.Count;
                                }

                                Volatile.Write(ref _lastDetectedHands, hands);
                                Volatile.Write(ref _lastDetectedPoints, points);
                            }
                            else
                            {
                                // Resultado explícito sem landmarks: só limpamos após alguns
                                // vazios consecutivos para reduzir piscadas por perdas pontuais.
                                var emptyStreak = Interlocked.Increment(ref _consecutiveEmptySnapshots);
                                if (emptyStreak >= EmptySnapshotClearThresholdFrames)
                                {
                                    Volatile.Write(ref _latestLandmarks, GestureLandmarkSnapshot.Empty);
                                    Volatile.Write(ref _lastDetectedHands, 0);
                                    Volatile.Write(ref _lastDetectedPoints, 0);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _mediaPipeFaulted = true;
                        PluginLogger.Log($"GestureProcessingPlugin: MediaPipe disabled after runtime error: {ex.Message}");
                        try
                        {
                            _graphRunner.Stop();
                        }
                        catch
                        {
                            // ignore stop failures after fault
                        }
                    }
                }

                var nowTick = Stopwatch.GetTimestamp();
                var nextPreviewTick = Volatile.Read(ref _nextPreviewRenderTick);
                if (nowTick >= nextPreviewTick)
                {
                    Volatile.Write(ref _nextPreviewRenderTick, nowTick + PreviewUpdateIntervalTicks);
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
            if (_frameCount % 120 == 0)
            {
                PluginLogger.Log($"GestureProcessingPlugin: PreviewLoop updating frame {frame.Width}x{frame.Height}");
            }
            _previewWindow.UpdateFrame(frame);
                        }
                        catch (Exception ex)
                        {
                            PluginLogger.Log($"GestureProcessingPlugin: failed to update preview window: {ex.Message}");
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }
    }

    private void RaiseToolbarStateChanged()
    {
        ToolbarStateChanged?.Invoke();
    }

    private void DrawLandmarksOverlay(PluginFrameContext context)
    {
        var snapshot = Volatile.Read(ref _latestLandmarks);

        if (!snapshot.HasLandmarks)
        {
            return;
        }

        var age = Volatile.Read(ref _framesSinceLastLandmarkUpdate);
        Interlocked.Increment(ref _framesSinceLastLandmarkUpdate);
        Volatile.Write(ref _lastOverlayAgeUs, age);

        if (age > MaxLandmarkAgFrames)
        {
            return;
        }

        var frameWidth = context.Frame.Width;
        var frameHeight = context.Frame.Height;
        OverlayPoint? firstHandBase = null;
        OverlayPoint? secondHandBase = null;

        Span<OverlayPoint> stackPoints = stackalloc OverlayPoint[21];

        foreach (var set in snapshot.Sets)
        {
            var pointsCount = set.Points.Count;
            if (pointsCount == 0)
            {
                continue;
            }

            if (pointsCount <= 21)
            {
                for (var i = 0; i < pointsCount; i++)
                {
                    stackPoints[i] = ToOverlayPoint(set.Points[i], frameWidth, frameHeight);
                }

                RegisterHandBasePoint(ref firstHandBase, ref secondHandBase, stackPoints[0]);

                foreach (var (a, b) in HandConnections)
                {
                    if (a >= pointsCount || b >= pointsCount)
                    {
                        continue;
                    }

                    context.Scene.Add(new OverlayLine(stackPoints[a], stackPoints[b], ConnectionStroke));
                }

                for (var i = 0; i < pointsCount; i++)
                {
                    context.Scene.Add(new OverlayCircle(stackPoints[i], 4f, LandmarkStroke, LandmarkFill));
                }

                DrawPalmOpennessCircle(context.Scene, stackPoints[..pointsCount]);

                continue;
            }

            var heapPoints = new OverlayPoint[pointsCount];
            for (var i = 0; i < pointsCount; i++)
            {
                heapPoints[i] = ToOverlayPoint(set.Points[i], frameWidth, frameHeight);
            }

            RegisterHandBasePoint(ref firstHandBase, ref secondHandBase, heapPoints[0]);

            foreach (var (a, b) in HandConnections)
            {
                if (a >= pointsCount || b >= pointsCount)
                {
                    continue;
                }

                context.Scene.Add(new OverlayLine(heapPoints[a], heapPoints[b], ConnectionStroke));
            }

            for (var i = 0; i < pointsCount; i++)
            {
                context.Scene.Add(new OverlayCircle(heapPoints[i], 4f, LandmarkStroke, LandmarkFill));
            }

            DrawPalmOpennessCircle(context.Scene, heapPoints.AsSpan());
        }

        if (firstHandBase is { } handA && secondHandBase is { } handB)
        {
            var stroke = SelectHandsDistanceStroke(handA, handB, frameWidth, frameHeight);
            context.Scene.Add(new OverlayLine(handA, handB, stroke));
        }
    }

    private static void RegisterHandBasePoint(ref OverlayPoint? firstHandBase, ref OverlayPoint? secondHandBase, OverlayPoint handBase)
    {
        if (firstHandBase is null)
        {
            firstHandBase = handBase;
            return;
        }

        if (secondHandBase is null)
        {
            secondHandBase = handBase;
        }
    }

    private static OverlayStroke SelectHandsDistanceStroke(OverlayPoint handA, OverlayPoint handB, int frameWidth, int frameHeight)
    {
        var dx = handA.X - handB.X;
        var dy = handA.Y - handB.Y;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy));
        var frameDiagonal = MathF.Sqrt((frameWidth * frameWidth) + (frameHeight * frameHeight));

        if (frameDiagonal <= 0f)
        {
            return HandsDistanceFarStroke;
        }

        var distanceRatio = distance / frameDiagonal;
        if (distanceRatio <= NearHandsDistanceRatio)
        {
            return HandsDistanceNearStroke;
        }

        if (distanceRatio <= MidHandsDistanceRatio)
        {
            return HandsDistanceMidStroke;
        }

        return HandsDistanceFarStroke;
    }

    private static void DrawPalmOpennessCircle(OverlayScene scene, ReadOnlySpan<OverlayPoint> points)
    {
        if (points.Length <= FingerTipIndices[^1])
        {
            return;
        }

        var palmCenter = AveragePoints(points, PalmCenterIndices);
        var averageTipDistance = AverageDistanceToCenter(points, FingerTipIndices, palmCenter);
        var palmBaseDistance = AverageDistanceToCenter(points, PalmBaseIndices, palmCenter);
        if (palmBaseDistance <= 0.001f)
        {
            return;
        }

        var opennessRatio = averageTipDistance / palmBaseDistance;
        var opennessNormalized = Math.Clamp(
            (opennessRatio - PalmOpenMinRatio) / (PalmOpenMaxRatio - PalmOpenMinRatio),
            0f,
            1f);

        var minRadius = palmBaseDistance * 0.55f;
        var maxRadius = palmBaseDistance * 1.45f;
        var radius = minRadius + ((maxRadius - minRadius) * opennessNormalized);
        radius = Math.Max(8f, radius);

        scene.Add(new OverlayCircle(palmCenter, radius, PalmOpenCircleStroke, PalmOpenCircleFill));
    }

    private static OverlayPoint AveragePoints(ReadOnlySpan<OverlayPoint> points, ReadOnlySpan<int> indices)
    {
        var sumX = 0f;
        var sumY = 0f;

        for (var i = 0; i < indices.Length; i++)
        {
            var p = points[indices[i]];
            sumX += p.X;
            sumY += p.Y;
        }

        var count = indices.Length;
        return OverlayPoint.FromPixels(sumX / count, sumY / count);
    }

    private static float AverageDistanceToCenter(ReadOnlySpan<OverlayPoint> points, ReadOnlySpan<int> indices, OverlayPoint center)
    {
        var total = 0f;
        for (var i = 0; i < indices.Length; i++)
        {
            var p = points[indices[i]];
            var dx = p.X - center.X;
            var dy = p.Y - center.Y;
            total += MathF.Sqrt((dx * dx) + (dy * dy));
        }

        return total / indices.Length;
    }

    private static OverlayPoint ToOverlayPoint(GestureLandmarkPoint point, int frameWidth, int frameHeight)
    {
        var x = Math.Clamp(point.X, 0f, 1f) * frameWidth;
        var y = Math.Clamp(point.Y, 0f, 1f) * frameHeight;
        return OverlayPoint.FromPixels(x, y);
    }

    private static double TicksToMs(long ticks)
    {
        if (ticks <= 0)
        {
            return 0d;
        }

        return (ticks * 1000d) / Stopwatch.Frequency;
    }

}