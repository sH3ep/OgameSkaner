using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using OgameSkaner.App.Views;

namespace OgameSkaner.App.Infrastructure;

/// <summary>User interaction that view models need but must not implement themselves (keeps them testable).</summary>
public interface IDialogService
{
    void ShowInfo(string message);

    void ShowError(string message);

    bool Confirm(string message);

    /// <returns>The chosen path, or null when cancelled.</returns>
    string? SaveFile(string filter, string defaultFileName);

    /// <returns>The chosen folder, or null when cancelled.</returns>
    string? PickFolder(string title);

    void ShowProfiles();
}

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    private const string Caption = "OgameSkaner";

    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? Application.Current?.MainWindow;

    public void ShowInfo(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message) => Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? SaveFile(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public void ShowProfiles()
    {
        var window = services.GetRequiredService<ProfilesWindow>();
        window.Owner = Owner;
        window.ShowDialog();
    }

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, Caption, buttons, image)
            : MessageBox.Show(message, Caption, buttons, image);
}
