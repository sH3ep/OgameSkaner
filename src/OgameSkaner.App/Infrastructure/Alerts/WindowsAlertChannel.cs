using System.Drawing;
using System.Windows.Forms;
using OgameSkaner.Core.Domain.Alerts;
using Application = System.Windows.Application;
using WindowState = System.Windows.WindowState;

namespace OgameSkaner.App.Infrastructure.Alerts;

/// <summary>
/// Shows alerts as Windows notifications: a balloon of the app's tray icon, which Windows 10/11 turns into a toast
/// in the notification center. Clicking it brings the main window to the front.
/// </summary>
public sealed class WindowsAlertChannel : IAlertChannel, IDisposable
{
    /// <summary>Limits of the balloon's title and text.</summary>
    private const int TitleLength = 63;
    private const int TextLength = 255;

    private NotifyIcon? _icon;

    public string Id => "windows";

    public string DisplayName => "Windows notification";

    public Task SendAsync(Alert alert, CancellationToken cancellationToken = default) =>
        Application.Current?.Dispatcher.InvokeAsync(() => Show(alert)).Task ?? Task.CompletedTask;

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    /// <summary>Runs on the UI thread: the tray icon needs its message loop.</summary>
    private void Show(Alert alert)
    {
        if (_icon is null)
        {
            _icon = new NotifyIcon { Icon = AppIcon(), Text = "OgameSkaner", Visible = true };
            _icon.BalloonTipClicked += (_, _) => ActivateMainWindow();
            _icon.DoubleClick += (_, _) => ActivateMainWindow();
        }

        var icon = alert.Severity switch
        {
            AlertSeverity.Critical => ToolTipIcon.Error,
            AlertSeverity.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info
        };
        _icon.ShowBalloonTip(10_000, Cut($"{alert.Universe}: {alert.Title}", TitleLength), Cut(alert.Message, TextLength), icon);
    }

    private static Icon AppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
                return icon;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
        }

        return SystemIcons.Warning;
    }

    private static void ActivateMainWindow()
    {
        if (Application.Current?.MainWindow is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
