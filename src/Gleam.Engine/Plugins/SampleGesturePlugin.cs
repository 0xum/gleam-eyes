using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;
using Gleam.Engine.Processing;

namespace Gleam.Engine.Plugins;

public sealed class SampleGesturePlugin : IFrameProcessingPlugin
{
    public string Name => "SampleGesture";

    public bool IsEnabled { get; set; } = true;

    public FrameProcessResult? Process(in RawFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return null;
        }

        var label = "Gesture: OpenHand";
        var confidence = 0.42f;
        return new FrameProcessResult(label, confidence);
    }

    public void BuildOverlays(in RawFrame frame, OverlayScene scene)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        var center = new OverlayPoint(frame.Width / 2f, frame.Height / 2f);
        var stroke = new OverlayStroke(3f, new ColorRgba(255, 180, 80, 220));
        var fill = new OverlayFill(new ColorRgba(255, 180, 80, 40));
        scene.Add(new OverlayCircle(center, MathF.Min(frame.Width, frame.Height) * 0.12f, stroke, fill));
    }
}