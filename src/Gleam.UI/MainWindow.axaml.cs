using Avalonia.Controls;
using System.ComponentModel;
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
}