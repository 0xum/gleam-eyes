using Gleam.Engine.Frames;

namespace Gleam.Engine.Capture;

public interface ICameraCapture : IDisposable
{
    event EventHandler<RawFrame>? FrameArrived;

    void Start();

    void Stop();
}