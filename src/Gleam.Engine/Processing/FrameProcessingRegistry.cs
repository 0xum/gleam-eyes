using System.Collections.Concurrent;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class FrameProcessingRegistry
{
    private readonly ConcurrentDictionary<string, IFrameProcessingPlugin> _plugins = new();

    public IReadOnlyCollection<IFrameProcessingPlugin> Plugins => _plugins.Values.ToArray();

    public bool Register(IFrameProcessingPlugin plugin)
        => _plugins.TryAdd(plugin.Name, plugin);

    public bool Unregister(string name)
        => _plugins.TryRemove(name, out _);

    public FrameProcessingResult Process(in RawFrame frame)
    {
        var scene = new OverlayScene();
        var results = new List<FrameProcessResult>();

        foreach (var plugin in _plugins.Values)
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            var result = plugin.Process(frame);
            if (result != null)
            {
                results.Add(result);
            }

            plugin.BuildOverlays(frame, scene);
        }

        return new FrameProcessingResult(scene, results);
    }
}