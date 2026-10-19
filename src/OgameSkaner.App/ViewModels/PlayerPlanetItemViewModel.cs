using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.App.ViewModels;

/// <summary>One row of the players list, with spy actions for the planet and its moon.</summary>
public sealed partial class PlayerPlanetItemViewModel(PlayerPlanet planet, IOgamexClient client, IActionQueue queue) : ObservableObject
{
    public PlayerPlanet Planet { get; } = planet;

    public string PlayerName => Planet.PlayerName;

    public string Coordinates => Planet.Coordinates.ToString();

    public bool HasMoon => Planet.HasMoon;

    /// <summary>The game's status letters ("i", "v", …) with the rank, e.g. "i #909".</summary>
    public string Status => string.Join(" ", new[] { Planet.Status.ToLetters(), Planet.Rank is { } rank ? $"#{rank:N0}" : "" }.Where(t => t.Length > 0));

    public string ScannedAt => $"Scanned {Planet.ScannedAt:g}";

    [ObservableProperty]
    private OperationStatus _planetSpyStatus;

    [ObservableProperty]
    private string? _planetSpyMessage;

    [ObservableProperty]
    private OperationStatus _moonSpyStatus;

    [ObservableProperty]
    private string? _moonSpyMessage;

    [RelayCommand]
    private async Task SpyPlanetAsync()
    {
        PlanetSpyStatus = OperationStatus.Pending;
        (PlanetSpyStatus, PlanetSpyMessage) = await SpyAsync(PlanetType.Planet);
    }

    [RelayCommand(CanExecute = nameof(HasMoon))]
    private async Task SpyMoonAsync()
    {
        MoonSpyStatus = OperationStatus.Pending;
        (MoonSpyStatus, MoonSpyMessage) = await SpyAsync(PlanetType.Moon);
    }

    private async Task<(OperationStatus, string)> SpyAsync(PlanetType type)
    {
        try
        {
            // The probes leave from the game's current planet: the player's, not one an automation switched to.
            await queue.RunAsync("Players", $"Spy on [{Planet.Coordinates}]", async ct =>
            {
                if (queue.HomePlanet is { } home && client.CurrentPlanet != home)
                    await client.SelectPlanetAsync(home, ct);
                await client.SendSpyAsync(Planet.Coordinates, type, ct);
            }, ActionPriority.User);
            return (OperationStatus.Succeeded, $"Spy probes sent at {DateTime.Now:T}");
        }
        catch (OgamexException ex)
        {
            return (OperationStatus.Failed, ex.Message);
        }
    }
}
