using Gleam.Engine.Frames;

namespace Gleam.Engine.Processing;

public interface IFrameProcessor
{
    FrameProcessResult Process(in RawFrame frame);
}