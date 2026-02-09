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
    private const int MaxInFlightFrames = 2;
    private const int MaxNoResultFramesBeforeResync = 6;
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
    private bool _hasQueueSizeMethod;
    private bool _isRunning;
    private long _processedFrames;
    private long _emptyFrames;
    private long _submittedFrames;
    private long _receivedResults;
    private long _framesSinceLastResult;
    private string? _previousCurrentDirectory;

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        EnsureExecutableMediapipeMirrorFromPlugin();
        ConfigureMediaPipeResourceRoot();
        SwitchWorkingDirectoryToPluginIfPossible();
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
        _hasQueueSizeMethod = _pollerQueueSizeMethod != null;
        _processedFrames = 0;
        _emptyFrames = 0;
        _submittedFrames = 0;
        _receivedResults = 0;
        _framesSinceLastResult = 0;
        _isRunning = true;
        PluginLogger.Log($"GestureProcessingPlugin: QueueSize method available: {_hasQueueSizeMethod}");
        PluginLogger.Log("GestureProcessingPlugin: MediaPipe graph started.");
    }

    public GestureLandmarkSnapshot Process(PreparedFrame frame)
    {
        if (!_isRunning || _graph == null || _poller == null)
        {
            return GestureLandmarkSnapshot.Empty;
        }

        _processedFrames++;

        // Step 1: Submit the frame.
        // O stream "multi_hand_landmarks" pode ficar sem emitir pacotes por alguns ciclos.
        // Se limitarmos estritamente por in-flight usando submitted/received, podemos entrar
        // em starvation quando o capture inicia sem mão visível. Fazemos um resync leve para
        // manter fluidez e recuperar detecção quando a mão entra depois.
        var inFlight = _submittedFrames - _receivedResults;
        var forceSubmitDuringStartupStall =
            _framesSinceLastResult >= MaxNoResultFramesBeforeResync &&
            (_framesSinceLastResult % 2 == 0);

        if (_framesSinceLastResult >= MaxNoResultFramesBeforeResync && inFlight >= MaxInFlightFrames)
        {
            // Sem resultados por vários frames, submitted/received pode ficar
            // desbalanceado porque o stream de landmarks não emite pacote em
            // todos os ciclos. Re-sincroniza contadores para evitar starvation.
            _receivedResults = _submittedFrames;
            inFlight = 0;
        }

        if (inFlight < MaxInFlightFrames || forceSubmitDuringStartupStall)
        {
            try
            {
                using var imageFrame = CreateImageFrame(frame);
                using var timestamp = new Timestamp(frame.TimestampNs / 1000);
                using var packet = new ImageFramePacket(imageFrame, timestamp);
                _graph.AddPacketToInputStream(InputStreamName, packet).AssertOk();
                _submittedFrames++;
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"MediaPipeGraphRunner: Error submitting frame: {ex.Message}");
            }
        }

        // Step 2: Collect results.
        // When QueueSize reflection is available, we know exactly how many packets
        // are ready and can drain them without blocking.
        // When QueueSize is NOT available, _poller.Next() would block indefinitely
        // waiting for a result. In that case we call Next() only when we are certain
        // a result must be pending (submittedFrames > receivedResults) and we limit
        // ourselves to exactly ONE call so we never block for more than one inference.
        NormalizedLandmarkListVectorPacket? latestResult = null;

        if (_hasQueueSizeMethod)
        {
            // Safe: QueueSize tells us exactly how many are ready (non-blocking).
            var drainCount = GetQueueSize();
            for (var i = 0; i < drainCount; i++)
            {
                var pkt = new NormalizedLandmarkListVectorPacket();
                if (_poller.Next(pkt))
                {
                    latestResult?.Dispose();
                    latestResult = pkt;
                    _receivedResults++;
                }
                else
                {
                    pkt.Dispose();
                    break;
                }
            }
        }
        else
        {
            // Fallback: no QueueSize. Next() blocks until a packet arrives.
            // Only call it if we know at least one result is expected.
            if (_submittedFrames > _receivedResults)
            {
                var pkt = new NormalizedLandmarkListVectorPacket();
                if (_poller.Next(pkt))
                {
                    latestResult = pkt;
                    _receivedResults++;
                }
                else
                {
                    pkt.Dispose();
                }
            }
        }

        // Step 3: Return the latest result if we got one.
        if (latestResult != null)
        {
            try
            {
                _framesSinceLastResult = 0;
                return CreateSnapshot(latestResult);
            }
            finally
            {
                latestResult.Dispose();
            }
        }

        _framesSinceLastResult++;

        return GestureLandmarkSnapshot.Empty;
    }

    private GestureLandmarkSnapshot CreateSnapshot(NormalizedLandmarkListVectorPacket packet)
    {
        var resultTimestampUs = packet.Timestamp().Microseconds;
        var sets = packet.Get()
            .Select(landmarkList => new GestureLandmarkSet(
                landmarkList.Landmark
                    .Select(landmark => new GestureLandmarkPoint(landmark.X, landmark.Y, landmark.Z))
                    .ToArray()))
            .ToArray();

        if (sets.Length == 0)
        {
            _emptyFrames++;
        }

        return new GestureLandmarkSnapshot(resultTimestampUs, sets);
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
            RestorePreviousWorkingDirectory();
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
        var pluginBaseDirectory = GetPluginBaseDirectory();
        var candidates = new[]
        {
            Path.Combine(pluginBaseDirectory, "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(pluginBaseDirectory, "Dependencies", "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "plugins", "gestures", "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "plugins", "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "mediapipe", "modules", "hand_landmark", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(pluginBaseDirectory, "mediapipe", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(pluginBaseDirectory, "Dependencies", "mediapipe", "hand_landmark_tracking_cpu.pbtxt"),
            Path.Combine(AppContext.BaseDirectory, "plugins", "gestures", "mediapipe", "hand_landmark_tracking_cpu.pbtxt"),
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
        var pluginBaseDirectory = GetPluginBaseDirectory();
        var candidateInPluginLocal = Path.Combine(pluginBaseDirectory, normalizedRelative);
        if (File.Exists(candidateInPluginLocal))
        {
            return candidateInPluginLocal;
        }

        var candidateInPluginDependencies = Path.Combine(pluginBaseDirectory, "Dependencies", normalizedRelative);
        if (File.Exists(candidateInPluginDependencies))
        {
            return candidateInPluginDependencies;
        }

        var candidateInPlugin = Path.Combine(AppContext.BaseDirectory, "plugins", "gestures", normalizedRelative);
        if (File.Exists(candidateInPlugin))
        {
            return candidateInPlugin;
        }

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
        var pluginBaseDirectory = GetPluginBaseDirectory();
        foreach (var relativePath in RequiredModelRelativePaths)
        {
            var candidateInPluginLocal = Path.Combine(pluginBaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var candidateInPluginDependencies = Path.Combine(pluginBaseDirectory, "Dependencies", relativePath.Replace('/', Path.DirectorySeparatorChar));
            var candidateInPlugin = Path.Combine(baseDir, "plugins", "gestures", relativePath.Replace('/', Path.DirectorySeparatorChar));
            var candidateInExe = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var candidateInPlugins = Path.Combine(baseDir, "plugins", relativePath.Replace('/', Path.DirectorySeparatorChar));
            var exists = File.Exists(candidateInPluginLocal) || File.Exists(candidateInPluginDependencies) || File.Exists(candidateInPlugin) || File.Exists(candidateInExe) || File.Exists(candidateInPlugins);

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

    private static void ConfigureMediaPipeResourceRoot()
    {
        var pluginBaseDirectory = GetPluginBaseDirectory();
        if (!Directory.Exists(Path.Combine(pluginBaseDirectory, "mediapipe"))
            && !Directory.Exists(Path.Combine(pluginBaseDirectory, "Dependencies", "mediapipe")))
        {
            return;
        }

        Environment.SetEnvironmentVariable("MEDIAPIPE_RESOURCE_DIR", pluginBaseDirectory);
    }

    private static void EnsureExecutableMediapipeMirrorFromPlugin()
    {
        var pluginBaseDirectory = GetPluginBaseDirectory();
        var pluginMediapipeDir = Path.Combine(pluginBaseDirectory, "mediapipe");
        if (!Directory.Exists(pluginMediapipeDir))
        {
            pluginMediapipeDir = Path.Combine(pluginBaseDirectory, "Dependencies", "mediapipe");
        }

        if (!Directory.Exists(pluginMediapipeDir))
        {
            return;
        }

        var appMediapipeDir = Path.Combine(AppContext.BaseDirectory, "mediapipe");
        if (Directory.Exists(appMediapipeDir))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(appMediapipeDir, pluginMediapipeDir);
            PluginLogger.Log($"GestureProcessingPlugin: linked MediaPipe assets into app base: {appMediapipeDir}");
            return;
        }
        catch
        {
            // fallback to physical copy when symlink is not allowed
        }

        CopyDirectoryRecursive(pluginMediapipeDir, appMediapipeDir);
        PluginLogger.Log($"GestureProcessingPlugin: copied MediaPipe assets into app base: {appMediapipeDir}");
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourceFile);
            var destinationFile = Path.Combine(destinationDir, relativePath);
            var destinationFolder = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private static string GetPluginBaseDirectory()
    {
        var assemblyLocation = typeof(MediaPipeGraphRunner).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyLocation);
        return string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory;
    }

    private void SwitchWorkingDirectoryToPluginIfPossible()
    {
        var pluginBaseDirectory = GetPluginBaseDirectory();
        if (!Directory.Exists(Path.Combine(pluginBaseDirectory, "mediapipe"))
            && !Directory.Exists(Path.Combine(pluginBaseDirectory, "Dependencies", "mediapipe")))
        {
            return;
        }

        try
        {
            _previousCurrentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(pluginBaseDirectory);
        }
        catch (Exception ex)
        {
            PluginLogger.Log($"GestureProcessingPlugin: unable to switch working directory for MediaPipe assets: {ex.Message}");
        }
    }

    private void RestorePreviousWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(_previousCurrentDirectory))
        {
            return;
        }

        try
        {
            Directory.SetCurrentDirectory(_previousCurrentDirectory);
        }
        catch
        {
            // no-op
        }
        finally
        {
            _previousCurrentDirectory = null;
        }
    }

    private int GetQueueSize()
    {
        if (_poller == null || _pollerQueueSizeMethod == null)
        {
            return 0;
        }

        try
        {
            var queueSizeRaw = _pollerQueueSizeMethod.Invoke(_poller, null);
            return queueSizeRaw switch
            {
                int size => size,
                long size => (int)size,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

}
