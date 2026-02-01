namespace Gleam.Engine.Processing;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginMetadataAttribute : Attribute
{
    public PluginMetadataAttribute(string name, string description, string version)
    {
        Name = name;
        Description = description;
        Version = version;
    }

    public string Name { get; }

    public string Description { get; }

    public string Version { get; }
}