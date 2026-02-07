using System.Runtime.InteropServices;
using System.Reflection;
using Gleam.Engine.Processing;
using Gleam.Gestures.Models;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Format;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;

namespace Gleam.Gestures.Processing;

internal sealed class MediaPipeGraphRunner : IDisposable
{
    private const string InputStreamName = "image";
    private const string OutputStreamName = "multi_hand_landmarks";
    private const int NoDetectionLogInterval = 120;
    private static readonly string[] RequiredModelRelativePaths =
    [
        "mediapipe/modules/hand_landmark/hand_landmark_full.tflite",
        "mediapipe/modules/hand_landmark/hand_landmark_lite.tflite",
        "mediapipe/modules/palm_detection/palm_detection_full.tflite",
        "mediapipe/modules/palm_detection/palm_detection_lite.tflite"
    ];

    private const string EmbeddedHandGraph = """
                                            input_stream: "IMAGE:image"
                                            output_stream: "multi_hand_landmarks"

                                            node {
                                              calculator: "ConstantSidePacketCalculator"
                                              output_side_packet: "PACKET:num_hands"
                                              node_options: {
                                                [type.googleapis.com/mediapipe.ConstantSidePacketCalculatorOptions]: {
                                                  packet { int_value: 1 }
                                                }
                                              }
                                            }

                                            node {
                                              calculator: "HandLandmarkTrackingCpu"
                                              input_stream: "IMAGE:image"
                                              input_side_packet: "NUM_HANDS:num_hands"
                                              output_stream: "LANDMARKS:multi_hand_landmarks"
                                              output_stream: "HANDEDNESS:multi_handedness"
                                              output_stream: "PALM_DETECTIONS:palm_detections"
                                              output_stream: "HAND_ROIS_FROM_LANDMARKS:hand_rects_from_landmarks"
                                              output_stream: "HAND_ROIS_FROM_PALM_DETECTIONS:hand_rects_from_palm_detections"
                                            }
                                            """;

    private CalculatorGraph? _graph;
    private OutputStreamPoller<List<NormalizedLandmarkList>>? _poller;
    private MethodInfo? _pollerQueueSizeMethod;
    private bool _isRunning;
    private long _processedFrames;
    private long _emptyFrames;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        LogAssetDiagnostics();

        var graphConfig = ResolveGraphConfig();
        graphConfig = NormalizeGraphAssetPaths(graphConfig);
        var graph = new CalculatorGraph(graphConfig);
        var poller = graph.AddOutputStreamPoller<List<NormalizedLandmarkList>>(OutputStreamName).Value();

        using var sidePacket = new SidePacket();
        sidePacket.Emplace("num_hands", new IntPacket(2));
        sidePacket.Emplace("model_complexity", new IntPacket(1));
        sidePacket.Emplace("use_prev_landmarks", new BoolPacket(true));
        graph.StartRun(sidePacket).AssertOk();

        _graph = graph;
        _poller = poller;
        _pollerQueueSizeMethod = poller.GetType().GetMethod("QueueSize", BindingFlags.Public | BindingFlags.Instance);
        _processedFrames = 0;
        _emptyFrames = 0;
        _isRunning = true;
        PluginLogger.Log("GestureProcessingPlugin: MediaPipe graph started.");
    }

    public GestureLandmarkSnapshot Process(PreparedFrame frame)
    {
        if (!_isRunning || _graph == null || _poller == null)
        {
            return GestureLandmarkSnapshot.Empty;
        }

        _processedFrames++;

        using var imageFrame = CreateImageFrame(frame);
        using var timestamp = new Timestamp(frame.TimestampNs / 1000);
        using var packet = new ImageFramePacket(imageFrame, timestamp);
        _graph.AddPacketToInputStream(InputStreamName, packet).AssertOk();

        if (!HasPendingOutput())
        {
            _emptyFrames++;
            MaybeLogNoDetections();
            return GestureLandmarkSnapshot.Empty;
        }

        var outputPacket = new NormalizedLandmarkListVectorPacket();
        if (!_poller.Next(outputPacket))
        {
            outputPacket.Dispose();
            _emptyFrames++;
            MaybeLogNoDetections();
            return GestureLandmarkSnapshot.Empty;
        }

        try
        {
            var sets = outputPacket.Get()
                .Select(landmarkList => new GestureLandmarkSet(
                    landmarkList.Landmark
                        .Select(landmark => new GestureLandmarkPoint(landmark.X, landmark.Y, landmark.Z))
                        .ToArray()))
                .ToArray();

            if (sets.Length == 0)
            {
                _emptyFrames++;
                MaybeLogNoDetections();
            }

            return new GestureLandmarkSnapshot(timestamp.Microseconds, sets);
        }
        finally
        {
            outputPacket.Dispose();
        }
    }

    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        try
        {
            _graph?.CloseInputStream(InputStreamName).AssertOk();
            _graph?.WaitUntilDone().AssertOk();
        }
        catch (Exception ex)
        {
            PluginLogger.Log($"GestureProcessingPlugin: MediaPipe stop warning: {ex.Message}");
        }
        finally
        {
            _poller?.Dispose();
            _poller = null;
            _pollerQueueSizeMethod = null;
            _graph?.Dispose();
            _graph = null;
            _isRunning = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static ImageFrame CreateImageFrame(PreparedFrame frame)
    {
        var imageFrame = new ImageFrame(ImageFormat.Types.Format.Srgb, frame.Width, frame.Height);
        Marshal.Copy(frame.Data, 0, imageFrame.MutablePixelData(), frame.Data.Length);
        return imageFrame;
    }

    private static string ResolveGraphConfig()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "plugins", "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "plugins", "mediapipe", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "mediapipe", "hand_landmark_tracking_cpu.pbtxt")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                PluginLogger.Log($"GestureProcessingPlugin: loading MediaPipe graph from '{candidate}'.");
                return File.ReadAllText(candidate);
            }
        }

        PluginLogger.Log("GestureProcessingPlugin: using embedded MediaPipe hand graph (CPU).");
        return EmbeddedHandGraph;
    }

    private static string NormalizeGraphAssetPaths(string graphConfig)
    {
        var normalized = graphConfig;
        foreach (var relativePath in RequiredModelRelativePaths)
        {
            var absolutePath = ResolveAssetAbsolutePath(relativePath);
            if (absolutePath == null)
            {
                continue;
            }

            normalized = normalized.Replace(relativePath, absolutePath.Replace('\\', '/'), StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string? ResolveAssetAbsolutePath(string relativePath)
    {
        var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidateInExe = Path.Combine(AppContext.BaseDirectory, normalizedRelative);
        if (File.Exists(candidateInExe))
        {
            return candidateInExe;
        }

        var candidateInPlugins = Path.Combine(AppContext.BaseDirectory, "plugins", normalizedRelative);
        return File.Exists(candidateInPlugins) ? candidateInPlugins : null;
    }

    private static void LogAssetDiagnostics()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var relativePath in RequiredModelRelativePaths)
        {
            var candidateInExe = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var candidateInPlugins = Path.Combine(baseDir, "plugins", relativePath.Replace('/', Path.DirectorySeparatorChar));
            var exists = File.Exists(candidateInExe) || File.Exists(candidateInPlugins);

            if (exists)
            {
                PluginLogger.Log($"GestureProcessingPlugin: MediaPipe asset OK: {relativePath}");
            }
            else
            {
                PluginLogger.Log($"GestureProcessingPlugin: MediaPipe asset MISSING: {relativePath}");
            }
        }
    }

    private void MaybeLogNoDetections()
    {
        if (_processedFrames == 0 || _processedFrames % NoDetectionLogInterval != 0)
        {
            return;
        }

        PluginLogger.Log(
            $"GestureProcessingPlugin: MediaPipe processed={_processedFrames}, no_landmarks={_emptyFrames}. If this stays high, verify .tflite assets and graph path.");
    }

    private bool HasPendingOutput()
    {
        if (_poller == null || _pollerQueueSizeMethod == null)
        {
            return true;
        }

        try
        {
            var queueSizeRaw = _pollerQueueSizeMethod.Invoke(_poller, null);
            return queueSizeRaw switch
            {
                int size => size > 0,
                long size => size > 0,
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }
}
