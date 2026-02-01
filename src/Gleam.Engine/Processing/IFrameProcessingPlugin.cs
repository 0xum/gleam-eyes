namespace Gleam.Engine.Processing;

public interface IFrameProcessingPlugin
{
    bool IsEnabled { get; set; }

    void OnStartCapture(PluginStartContext context);

    void OnUpdateCapture(PluginFrameContext context);

    void OnEndCapture(PluginEndContext context);
}