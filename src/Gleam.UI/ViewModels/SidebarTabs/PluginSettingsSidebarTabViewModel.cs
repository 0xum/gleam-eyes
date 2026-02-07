using Avalonia.Controls;

namespace Gleam.Ui.ViewModels.SidebarTabs;

public sealed class PluginSettingsSidebarTabViewModel : SidebarTabViewModel
{
    public PluginSettingsSidebarTabViewModel(string title, string pluginName, Control view) : base(title)
    {
        PluginName = pluginName;
        View = view;
    }

    public string PluginName { get; }

    public Control View { get; }
}