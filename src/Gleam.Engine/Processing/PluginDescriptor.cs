namespace Gleam.Engine.Processing;

public sealed class PluginDescriptor
{
    public PluginDescriptor(PluginMetadataAttribute metadata, IFrameProcessingPlugin instance, string source)
    {
        Metadata = metadata;
        Instance = instance;
        Source = source;
    }

    public PluginMetadataAttribute Metadata { get; }

    public IFrameProcessingPlugin Instance { get; }

    public string Source { get; }
}