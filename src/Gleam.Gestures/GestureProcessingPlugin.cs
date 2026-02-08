using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Controls;
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
    private const float NearHandsDistanceRatio = 0.18f;
    private const float MidHandsDistanceRatio = 0.35f;
    private const float PalmOpenMinRatio = 1.05f;
    private const float PalmOpenMaxRatio = 1.95f;
    private const int InteractiveCircleCount = 4;
    private const float InteractiveCircleMinRadiusRatio = 0.07f;
    private const float InteractiveCircleMaxRadiusRatio = 0.12f;
    private const float InteractiveCirclePaddingRatio = 0.05f;
    private const float PinchCloseDistanceRatio = 0.95f;
    private const float PinchReleaseDistanceMultiplier = 1.45f;
    private static readonly OverlayStroke LandmarkStroke = new(1.5f, new ColorRgba(80, 255, 160, 180));
    private static readonly OverlayStroke ConnectionStroke = new(2.2f, new ColorRgba(255, 180, 80, 210));
    private static readonly OverlayStroke HandsDistanceNearStroke = new(4f, new ColorRgba(80, 255, 120, 230));
    private static readonly OverlayStroke HandsDistanceMidStroke = new(4f, new ColorRgba(255, 220, 80, 230));
    private static readonly OverlayStroke HandsDistanceFarStroke = new(4f, new ColorRgba(255, 80, 80, 230));
    private static readonly OverlayStroke PalmOpenCircleStroke = new(3f, new ColorRgba(90, 200, 255, 220));
    private static readonly OverlayFill PalmOpenCircleFill = new(new ColorRgba(90, 200, 255, 48));
    private static readonly OverlayFill LandmarkFill = new(new ColorRgba(80, 255, 160, 80));
    private static readonly OverlayStroke InteractiveCircleIdleStroke = new(2.8f, new ColorRgba(180, 180, 255, 220));
    private static readonly OverlayFill InteractiveCircleIdleFill = new(new ColorRgba(160, 160, 255, 42));
    private static readonly OverlayStroke InteractiveCircleHoverStroke = new(3.4f, new ColorRgba(255, 220, 90, 240));
    private static readonly OverlayFill InteractiveCircleHoverFill = new(new ColorRgba(255, 220, 90, 64));
    private static readonly OverlayStroke InteractiveCircleDragStroke = new(4f, new ColorRgba(80, 255, 140, 245));
    private static readonly OverlayFill InteractiveCircleDragFill = new(new ColorRgba(80, 255, 140, 92));
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
    private readonly Random _random = new();
    private readonly List<InteractiveCircle> _interactiveCircles = new();
    private bool _interactiveCirclesInitialized;
    private int _interactiveFrameWidth;
    private int _interactiveFrameHeight;
    private int? _activeDraggedCircleIndex;
    private volatile bool _renderHandSkeletonOverlay;

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

    public string SettingsTabTitle => "Gesture Settings";

    public bool RenderHandSkeletonOverlay
    {
        get => _renderHandSkeletonOverlay;
        set => _renderHandSkeletonOverlay = value;
    }

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

    public Control CreateSettingsView()
    {
        var toggle = new CheckBox
        {
            Content = "Render hand skeleton overlay",
            IsChecked = RenderHandSkeletonOverlay
        };

        toggle.IsCheckedChanged += (_, _) =>
        {
            RenderHandSkeletonOverlay = toggle.IsChecked == true;
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Gesture plugin settings"
                },
                toggle
            }
        };
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
        _interactiveCirclesInitialized = false;
        _interactiveCircles.Clear();
        _activeDraggedCircleIndex = null;
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
        _activeDraggedCircleIndex = null;

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
        var renderHandSkeletonOverlay = RenderHandSkeletonOverlay;
        var frameWidth = context.Frame.Width;
        var frameHeight = context.Frame.Height;
        EnsureInteractiveCircles(frameWidth, frameHeight);

        var snapshot = Volatile.Read(ref _latestLandmarks);
        var handPinches = new List<HandPinchState>(snapshot.HasLandmarks ? snapshot.Sets.Count : 0);

        OverlayPoint? firstHandBase = null;
        OverlayPoint? secondHandBase = null;

        if (snapshot.HasLandmarks)
        {
            var age = Volatile.Read(ref _framesSinceLastLandmarkUpdate);
            Interlocked.Increment(ref _framesSinceLastLandmarkUpdate);
            Volatile.Write(ref _lastOverlayAgeUs, age);

            if (age <= MaxLandmarkAgFrames)
            {
                Span<OverlayPoint> stackPoints = stackalloc OverlayPoint[21];
                var handIndex = 0;

                foreach (var set in snapshot.Sets)
                {
                    var pointsCount = set.Points.Count;
                    if (pointsCount == 0)
                    {
                        handIndex++;
                        continue;
                    }

                    if (pointsCount <= 21)
                    {
                        for (var i = 0; i < pointsCount; i++)
                        {
                            stackPoints[i] = ToOverlayPoint(set.Points[i], frameWidth, frameHeight);
                        }

                        if (renderHandSkeletonOverlay)
                        {
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
                        }

                        RegisterHandPinchState(handPinches, stackPoints[..pointsCount], handIndex);

                        handIndex++;
                        continue;
                    }

                    var heapPoints = new OverlayPoint[pointsCount];
                    for (var i = 0; i < pointsCount; i++)
                    {
                        heapPoints[i] = ToOverlayPoint(set.Points[i], frameWidth, frameHeight);
                    }

                    if (renderHandSkeletonOverlay)
                    {
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

                    RegisterHandPinchState(handPinches, heapPoints.AsSpan(), handIndex);
                    handIndex++;
                }

                if (renderHandSkeletonOverlay && firstHandBase is { } handA && secondHandBase is { } handB)
                {
                    var stroke = SelectHandsDistanceStroke(handA, handB, frameWidth, frameHeight);
                    context.Scene.Add(new OverlayLine(handA, handB, stroke));
                }
            }
        }

        UpdateInteractiveCircles(handPinches, frameWidth, frameHeight);
        DrawInteractiveCircles(context.Scene, handPinches);
    }

    private void EnsureInteractiveCircles(int frameWidth, int frameHeight)
    {
        if (_interactiveCirclesInitialized && frameWidth == _interactiveFrameWidth && frameHeight == _interactiveFrameHeight)
        {
            return;
        }

        _interactiveFrameWidth = frameWidth;
        _interactiveFrameHeight = frameHeight;
        _interactiveCirclesInitialized = true;
        _activeDraggedCircleIndex = null;
        _interactiveCircles.Clear();

        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return;
        }

        var minFrameSide = MathF.Max(1f, MathF.Min(frameWidth, frameHeight));
        var minRadius = minFrameSide * InteractiveCircleMinRadiusRatio;
        var maxRadius = minFrameSide * InteractiveCircleMaxRadiusRatio;
        var padding = minFrameSide * InteractiveCirclePaddingRatio;

        for (var i = 0; i < InteractiveCircleCount; i++)
        {
            var radius = NextFloat(minRadius, maxRadius);
            var minX = radius + padding;
            var maxX = frameWidth - radius - padding;
            var minY = radius + padding;
            var maxY = frameHeight - radius - padding;

            var centerX = maxX > minX
                ? NextFloat(minX, maxX)
                : frameWidth * 0.5f;
            var centerY = maxY > minY
                ? NextFloat(minY, maxY)
                : frameHeight * 0.5f;

            _interactiveCircles.Add(new InteractiveCircle(OverlayPoint.FromPixels(centerX, centerY), radius));
        }
    }

    private void DrawInteractiveCircles(OverlayScene scene, IReadOnlyList<HandPinchState> handPinches)
    {
        if (_interactiveCircles.Count == 0)
        {
            return;
        }

        int? hoveredCircle = null;
        for (var i = 0; i < _interactiveCircles.Count; i++)
        {
            var circle = _interactiveCircles[i];
            for (var handIndex = 0; handIndex < handPinches.Count; handIndex++)
            {
                var hand = handPinches[handIndex];
                if (IsPointInsideCircle(hand.ThumbTip, circle) || IsPointInsideCircle(hand.IndexTip, circle))
                {
                    hoveredCircle = i;
                    break;
                }
            }

            if (hoveredCircle.HasValue)
            {
                break;
            }
        }

        for (var i = 0; i < _interactiveCircles.Count; i++)
        {
            var circle = _interactiveCircles[i];

            var (stroke, fill) = i == _activeDraggedCircleIndex
                ? (InteractiveCircleDragStroke, InteractiveCircleDragFill)
                : (i == hoveredCircle ? (InteractiveCircleHoverStroke, InteractiveCircleHoverFill) : (InteractiveCircleIdleStroke, InteractiveCircleIdleFill));

            scene.Add(new OverlayCircle(circle.Center, circle.Radius, stroke, fill));
        }
    }

    private void UpdateInteractiveCircles(IReadOnlyList<HandPinchState> handPinches, int frameWidth, int frameHeight)
    {
        if (_interactiveCircles.Count == 0)
        {
            _activeDraggedCircleIndex = null;
            return;
        }

        if (_activeDraggedCircleIndex is { } activeCircleIndex)
        {
            if (activeCircleIndex < 0 || activeCircleIndex >= _interactiveCircles.Count)
            {
                _activeDraggedCircleIndex = null;
                return;
            }

            var activeCircle = _interactiveCircles[activeCircleIndex];
            var dragHand = FindClosestPinchingHand(handPinches, activeCircle.Center, useReleaseThreshold: true);
            if (dragHand is { } handState)
            {
                activeCircle.Center = ClampToFrame(handState.PinchCenter, frameWidth, frameHeight, activeCircle.Radius);
                return;
            }

            _activeDraggedCircleIndex = null;
        }

        for (var handListIndex = 0; handListIndex < handPinches.Count; handListIndex++)
        {
            var hand = handPinches[handListIndex];
            if (hand.PinchDistance > hand.PinchCloseThreshold)
            {
                continue;
            }

            for (var circleIndex = 0; circleIndex < _interactiveCircles.Count; circleIndex++)
            {
                var circle = _interactiveCircles[circleIndex];
                var thumbInside = IsPointInsideCircle(hand.ThumbTip, circle);
                var indexInside = IsPointInsideCircle(hand.IndexTip, circle);

                if (!thumbInside && !indexInside)
                {
                    var pinchInside = IsPointInsideCircle(hand.PinchCenter, circle);
                    if (!pinchInside)
                    {
                        continue;
                    }
                }

                _activeDraggedCircleIndex = circleIndex;
                circle.Center = ClampToFrame(hand.PinchCenter, frameWidth, frameHeight, circle.Radius);
                return;
            }
        }
    }

    private static HandPinchState? FindClosestPinchingHand(
        IReadOnlyList<HandPinchState> handPinches,
        OverlayPoint reference,
        bool useReleaseThreshold)
    {
        HandPinchState? best = null;
        var bestDistanceSq = float.MaxValue;

        for (var i = 0; i < handPinches.Count; i++)
        {
            var hand = handPinches[i];
            var threshold = useReleaseThreshold ? hand.PinchReleaseThreshold : hand.PinchCloseThreshold;
            if (hand.PinchDistance > threshold)
            {
                continue;
            }

            var dx = hand.PinchCenter.X - reference.X;
            var dy = hand.PinchCenter.Y - reference.Y;
            var distanceSq = (dx * dx) + (dy * dy);
            if (distanceSq >= bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            best = hand;
        }

        return best;
    }

    private void RegisterHandPinchState(List<HandPinchState> handPinches, ReadOnlySpan<OverlayPoint> points, int handIndex)
    {
        if (points.Length <= 8)
        {
            return;
        }

        var thumbTip = points[4];
        var indexTip = points[8];
        var pinchDx = thumbTip.X - indexTip.X;
        var pinchDy = thumbTip.Y - indexTip.Y;
        var pinchDistance = MathF.Sqrt((pinchDx * pinchDx) + (pinchDy * pinchDy));
        var pinchCenter = OverlayPoint.FromPixels((thumbTip.X + indexTip.X) * 0.5f, (thumbTip.Y + indexTip.Y) * 0.5f);

        var palmCenter = AveragePoints(points, PalmCenterIndices);
        var palmBaseDistance = AverageDistanceToCenter(points, PalmBaseIndices, palmCenter);
        if (palmBaseDistance <= 0.001f)
        {
            return;
        }

        var closeThreshold = Math.Max(16f, palmBaseDistance * PinchCloseDistanceRatio);
        var releaseThreshold = closeThreshold * PinchReleaseDistanceMultiplier;
        handPinches.Add(new HandPinchState(handIndex, thumbTip, indexTip, pinchCenter, pinchDistance, closeThreshold, releaseThreshold));
    }

    private static OverlayPoint ClampToFrame(OverlayPoint point, int frameWidth, int frameHeight, float margin)
    {
        var minX = Math.Max(0f, margin);
        var maxX = Math.Max(minX, frameWidth - margin);
        var minY = Math.Max(0f, margin);
        var maxY = Math.Max(minY, frameHeight - margin);
        var x = Math.Clamp(point.X, minX, maxX);
        var y = Math.Clamp(point.Y, minY, maxY);
        return OverlayPoint.FromPixels(x, y);
    }

    private static bool IsPointInsideCircle(OverlayPoint point, InteractiveCircle circle)
    {
        var dx = point.X - circle.Center.X;
        var dy = point.Y - circle.Center.Y;
        return ((dx * dx) + (dy * dy)) <= (circle.Radius * circle.Radius);
    }

    private float NextFloat(float min, float max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + ((float)_random.NextDouble() * (max - min));
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

    private sealed class InteractiveCircle
    {
        public InteractiveCircle(OverlayPoint center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public OverlayPoint Center { get; set; }

        public float Radius { get; }
    }

    private readonly record struct HandPinchState(
        int HandIndex,
        OverlayPoint ThumbTip,
        OverlayPoint IndexTip,
        OverlayPoint PinchCenter,
        float PinchDistance,
        float PinchCloseThreshold,
        float PinchReleaseThreshold);

}