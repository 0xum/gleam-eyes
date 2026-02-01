using Avalonia.Controls;

namespace Gleam.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new ViewModels.MainWindowViewModel();
    }
}