using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class PluginFrameContext
{
    public PluginFrameContext(RawFrame frame, OverlayScene scene)
    {
        Frame = frame;
        Scene = scene;
    }

    public RawFrame Frame { get; }

    public OverlayScene Scene { get; }
}