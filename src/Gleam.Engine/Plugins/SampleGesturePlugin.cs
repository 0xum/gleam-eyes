using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;
using Gleam.Engine.Processing;

namespace Gleam.Engine.Plugins;

[PluginMetadata("SampleGesture", "Exemplo básico de plugin com overlay", "1.0.0")]
public sealed class SampleGesturePlugin : IFrameProcessingPlugin
{
    public bool IsEnabled { get; set; } = true;

    public void OnStartCapture(PluginStartContext context)
    {
    }
    public void OnUpdateCapture(PluginFrameContext context)
    {
        var frame = context.Frame;
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        var center = new OverlayPoint(frame.Width / 2f, frame.Height / 2f);
        var stroke = new OverlayStroke(3f, new ColorRgba(255, 180, 80, 220));
        var fill = new OverlayFill(new ColorRgba(255, 180, 80, 40));
        context.Scene.Add(new OverlayCircle(center, MathF.Min(frame.Width, frame.Height) * 0.12f, stroke, fill));
    }

    public void OnEndCapture(PluginEndContext context)
    {
    }
}