namespace Gleam.Ui.ViewModels.SidebarTabs;

public abstract class SidebarTabViewModel
{
    protected SidebarTabViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}