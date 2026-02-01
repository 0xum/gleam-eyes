namespace Gleam.Engine.Processing;

public sealed class PluginEndContext
{
    public PluginEndContext(DateTimeOffset endedAt)
    {
        EndedAt = endedAt;
    }

    public DateTimeOffset EndedAt { get; }
}