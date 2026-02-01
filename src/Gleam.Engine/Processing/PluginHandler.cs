using System.Reflection;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class PluginHandler
{
    private readonly List<PluginDescriptor> _plugins = new();
    private readonly HashSet<string> _pluginKeys = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    public void LoadPlugins()
    {
        _plugins.Clear();
        _pluginKeys.Clear();
        var assemblies = new List<Assembly> { Assembly.GetExecutingAssembly() };
        var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(pluginDirectory))
        {
            foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll"))
            {
                try
                {
                    assemblies.Add(Assembly.LoadFrom(dllPath));
                }
                catch
                {
                    // Ignore invalid plugin assemblies for now.
                }
            }
        }

        foreach (var assembly in assemblies)
        {
            foreach (var descriptor in DiscoverPlugins(assembly, assembly.Location))
            {
                RegisterDescriptor(descriptor);
            }
        }
    }

    public void RegisterPlugin(IFrameProcessingPlugin plugin, PluginMetadataAttribute metadata, string source)
    {
        RegisterDescriptor(new PluginDescriptor(metadata, plugin, source));
    }

    public void OnStartCapture()
    {
        var context = new PluginStartContext(DateTimeOffset.UtcNow);
        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            plugin.OnStartCapture(context);
        }
    }

    public FrameProcessingResult OnUpdateCapture(in RawFrame frame)
    {
        var scene = new OverlayScene();
        var context = new PluginFrameContext(frame, scene);

        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            plugin.OnUpdateCapture(context);
        }

        return new FrameProcessingResult(scene, Array.Empty<FrameProcessResult>());
    }

    public void OnEndCapture()
    {
        var context = new PluginEndContext(DateTimeOffset.UtcNow);
        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            plugin.OnEndCapture(context);
        }
    }

    private void RegisterDescriptor(PluginDescriptor descriptor)
    {
        var key = $"{descriptor.Metadata.Name}:{descriptor.Source}";
        if (_pluginKeys.Add(key))
        {
            _plugins.Add(descriptor);
        }
    }

    private static IEnumerable<PluginDescriptor> DiscoverPlugins(Assembly assembly, string source)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }

            if (!typeof(IFrameProcessingPlugin).IsAssignableFrom(type))
            {
                continue;
            }

            var metadata = type.GetCustomAttribute<PluginMetadataAttribute>();
            if (metadata == null)
            {
                continue;
            }

            if (Activator.CreateInstance(type) is IFrameProcessingPlugin instance)
            {
                yield return new PluginDescriptor(metadata, instance, source);
            }
        }
    }
}