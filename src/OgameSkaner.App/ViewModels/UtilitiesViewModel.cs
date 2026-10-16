using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;

namespace OgameSkaner.App.ViewModels;

public sealed partial class UtilitiesViewModel(
    IUniverseContext context,
    FleetEscapeService fleetEscape,
    IDialogService dialogs) : PageViewModel
{
    public ObservableCollection<string> Log { get; } = [];

    [RelayCommand]
    private async Task EscapeFleetToMoonAsync()
    {
        if (context.Client is not { } client)
        {
            dialogs.ShowError("Select a universe first.");
            return;
        }

        if (!dialogs.Confirm($"Send every ship of the current planet on {client.Universe.Name} to its moon?"))
            return;

        AddLog("Fleet escape started");
        try
        {
            var result = await fleetEscape.EscapeToMoonAsync(client);
            AddLog(result.Message);
        }
        catch (OgamexException ex)
        {
            AddLog($"Fleet escape failed: {ex.Message}");
        }
    }

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}
