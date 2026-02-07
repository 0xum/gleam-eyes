using Avalonia.Controls;
using System.ComponentModel;
using System.Text;
using Gleam.Ui.ViewModels;

namespace Gleam.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainWindowViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.PreviewImage))
        {
            PreviewImage?.InvalidateVisual();
        }
    }

    private async void CopyLogsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var sb = new StringBuilder();
        foreach (var line in viewModel.PluginLogs)
        {
            sb.AppendLine(line);
        }

        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (Clipboard != null)
        {
            await Clipboard.SetTextAsync(text);
        }
    }
}