using System.Collections.Concurrent;
using Gleam.Engine.Frames;

namespace Gleam.Engine.Overlays;

public sealed class OverlayModuleRegistry
{
    private readonly ConcurrentDictionary<string, IOverlayModule> _modules = new();

    public IReadOnlyCollection<IOverlayModule> Modules => _modules.Values.ToArray();

    public bool Register(IOverlayModule module)
        => _modules.TryAdd(module.Name, module);

    public bool Unregister(string name)
        => _modules.TryRemove(name, out _);

    public OverlayScene BuildScene(in RawFrame frame)
    {
        var scene = new OverlayScene();
        foreach (var module in _modules.Values)
        {
            if (!module.IsEnabled)
            {
                continue;
            }

            module.BuildOverlays(frame, scene);
        }

        return scene;
    }
}