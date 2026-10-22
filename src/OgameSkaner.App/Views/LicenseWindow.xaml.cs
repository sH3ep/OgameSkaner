using System.Windows;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow(LicenseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
