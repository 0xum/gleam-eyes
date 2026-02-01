using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Modules;

public sealed class BasicGridOverlayModule : IOverlayModule
{
    public string Name => "BasicGrid";

    public bool IsEnabled { get; set; } = true;

    public void BuildOverlays(in RawFrame frame, OverlayScene scene)
    {
        var stroke = new OverlayStroke(1f, new ColorRgba(0, 255, 180, 200));
        var step = Math.Max(40, frame.Width / 12);

        for (var x = step; x < frame.Width; x += step)
        {
            scene.Add(new OverlayLine(new OverlayPoint(x, 0), new OverlayPoint(x, frame.Height), stroke));
        }

        for (var y = step; y < frame.Height; y += step)
        {
            scene.Add(new OverlayLine(new OverlayPoint(0, y), new OverlayPoint(frame.Width, y), stroke));
        }
    }
}