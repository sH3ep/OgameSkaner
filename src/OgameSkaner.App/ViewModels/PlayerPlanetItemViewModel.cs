using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;

namespace OgameSkaner.App.ViewModels;

/// <summary>One row of the players list, with spy actions for the planet and its moon.</summary>
public sealed partial class PlayerPlanetItemViewModel(PlayerPlanet planet, IOgamexClient client) : ObservableObject
{
    public PlayerPlanet Planet { get; } = planet;

    public string PlayerName => Planet.PlayerName;

    public string Coordinates => Planet.Coordinates.ToString();

    public bool HasMoon => Planet.HasMoon;

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
            await client.SendSpyAsync(Planet.Coordinates, type);
            return (OperationStatus.Succeeded, $"Spy probes sent at {DateTime.Now:T}");
        }
        catch (OgamexException ex)
        {
            return (OperationStatus.Failed, ex.Message);
        }
    }
}
