using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class OverlayModulePluginAdapter : IFrameProcessingPlugin
{
    private readonly IOverlayModule _module;

    public OverlayModulePluginAdapter(IOverlayModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public string Name => _module.Name;

    public bool IsEnabled
    {
        get => _module.IsEnabled;
        set => _module.IsEnabled = value;
    }

    public FrameProcessResult? Process(in RawFrame frame)
        => null;

    public void BuildOverlays(in RawFrame frame, OverlayScene scene)
        => _module.BuildOverlays(frame, scene);
}