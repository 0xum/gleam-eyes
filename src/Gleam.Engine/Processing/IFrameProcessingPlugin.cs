using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public interface IFrameProcessingPlugin
{
    string Name { get; }

    bool IsEnabled { get; set; }

    FrameProcessResult? Process(in RawFrame frame);

    void BuildOverlays(in RawFrame frame, OverlayScene scene);
}