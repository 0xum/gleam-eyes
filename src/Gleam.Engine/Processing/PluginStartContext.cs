namespace Gleam.Engine.Processing;

public sealed class PluginStartContext
{
    public PluginStartContext(DateTimeOffset startedAt)
    {
        StartedAt = startedAt;
    }

    public DateTimeOffset StartedAt { get; }
}