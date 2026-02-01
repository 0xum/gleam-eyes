using Gleam.Engine.Frames;

namespace Gleam.Engine.Overlays;

public interface IOverlayModule
{
    string Name { get; }

    bool IsEnabled { get; }

    void BuildOverlays(in RawFrame frame, OverlayScene scene);
}