using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Processing;

public sealed class PluginHandler
{
    private readonly List<PluginDescriptor> _plugins = new();
    private readonly HashSet<string> _pluginKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _resolverRegistered;

    public IReadOnlyList<PluginDescriptor> Plugins => _plugins;

    public void LoadPlugins()
    {
        _plugins.Clear();
        _pluginKeys.Clear();
        var assemblies = new List<Assembly> { Assembly.GetExecutingAssembly() };
        var pluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");
        RegisterPluginResolvers(pluginDirectory);
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
        PluginLogger.Log("[PluginHandler] OnStartCapture invoked.");
        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            try
            {
                plugin.OnStartCapture(context);
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"[PluginHandler] OnStartCapture error ({plugin.GetType().Name}): {ex}\n{ex.StackTrace}");
            }
        }
    }

    public FrameProcessingResult OnUpdateCapture(in RawFrame frame)
    {
        var scene = new OverlayScene();
        var context = new PluginFrameContext(frame, scene);

        PluginLogger.Log("[PluginHandler] OnUpdateCapture tick.");
        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            try
            {
                plugin.OnUpdateCapture(context);
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"[PluginHandler] OnUpdateCapture error ({plugin.GetType().Name}): {ex}\n{ex.StackTrace}");
            }
        }

        return new FrameProcessingResult(scene, Array.Empty<FrameProcessResult>());
    }

    public void OnEndCapture()
    {
        var context = new PluginEndContext(DateTimeOffset.UtcNow);
        PluginLogger.Log("[PluginHandler] OnEndCapture invoked.");
        foreach (var plugin in _plugins.Select(p => p.Instance))
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            try
            {
                plugin.OnEndCapture(context);
            }
            catch (Exception ex)
            {
                PluginLogger.Log($"[PluginHandler] OnEndCapture error ({plugin.GetType().Name}): {ex}\n{ex.StackTrace}");
            }
        }
    }

    private void RegisterPluginResolvers(string pluginDirectory)
    {
        if (_resolverRegistered || string.IsNullOrWhiteSpace(pluginDirectory))
        {
            return;
        }

        _resolverRegistered = true;

        var resolvers = new Dictionary<string, AssemblyDependencyResolver>(StringComparer.OrdinalIgnoreCase);

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            foreach (var resolver in resolvers.Values)
            {
                var resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
                if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(resolvedPath);
                }
            }

            var candidate = Path.Combine(pluginDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
        };

        NativeLibrary.SetDllImportResolver(typeof(PluginHandler).Assembly, (libraryName, _, _) =>
        {
            foreach (var resolver in resolvers.Values)
            {
                var resolvedPath = resolver.ResolveUnmanagedDllToPath(libraryName);
                if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                {
                    return NativeLibrary.Load(resolvedPath);
                }
            }

            var nativeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "mediapipe_c.dll"
                : "libmediapipe_c.so";
            var candidate = Path.Combine(pluginDirectory, nativeName);
            return File.Exists(candidate) ? NativeLibrary.Load(candidate) : IntPtr.Zero;
        });

        if (Directory.Exists(pluginDirectory))
        {
            foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll"))
            {
                if (!resolvers.ContainsKey(dllPath))
                {
                    resolvers[dllPath] = new AssemblyDependencyResolver(dllPath);
                }
            }
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