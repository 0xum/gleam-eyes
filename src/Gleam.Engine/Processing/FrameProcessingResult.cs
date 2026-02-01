using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class FrameProcessingResult
{
    public FrameProcessingResult(OverlayScene scene, IReadOnlyList<FrameProcessResult> results)
    {
        Scene = scene;
        Results = results;
    }

    public OverlayScene Scene { get; }

    public IReadOnlyList<FrameProcessResult> Results { get; }
}