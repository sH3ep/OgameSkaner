using System.Windows;
using OgameSkaner.App.ViewModels;

namespace OgameSkaner.App.Views;

public partial class ProfilesWindow : Window
{
    public ProfilesWindow(ProfilesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
