using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class OverlayModulePluginAdapter : IFrameProcessingPlugin
{
    private readonly IOverlayModule _module;

    public OverlayModulePluginAdapter(IOverlayModule module)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
    }

    public bool IsEnabled
    {
        get => _module.IsEnabled;
        set => _module.IsEnabled = value;
    }

    public void OnStartCapture(PluginStartContext context)
    {
    }

    public void OnUpdateCapture(PluginFrameContext context)
        => _module.BuildOverlays(context.Frame, context.Scene);

    public void OnEndCapture(PluginEndContext context)
    {
    }
}