using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Gleam.Engine.Frames;

namespace Gleam.Engine.Pipeline;

public sealed class FramePipeline
{
    private readonly Channel<RawFrame> _channel;

    public FramePipeline()
    {
        var options = new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        };

        _channel = Channel.CreateBounded<RawFrame>(options);
    }

    public event EventHandler<RawFrame>? LatestFrameArrived;

    public bool TryWrite(RawFrame frame)
    {
        var written = _channel.Writer.TryWrite(frame);
        if (written)
        {
            LatestFrameArrived?.Invoke(this, frame);
        }

        return written;
    }

    public async IAsyncEnumerable<RawFrame> ReadAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var frame))
            {
                yield return frame;
            }
        }
    }
}