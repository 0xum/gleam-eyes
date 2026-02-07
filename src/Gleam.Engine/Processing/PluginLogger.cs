namespace Gleam.Engine.Processing;

public static class PluginLogger
{
    public static event Action<string>? Message;

    public static void Log(string message)
    {
        Message?.Invoke(message);
    }
}