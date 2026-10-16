using System.Windows;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
