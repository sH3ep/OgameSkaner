using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>Orders the same ships or defenses on several planets at once.</summary>
public sealed partial class ShipyardViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly ShipyardService _shipyard;
    private readonly OwnPlanetService _ownPlanets;
    private readonly IDialogService _dialogs;

    /// <summary>Planets the user unticked, so new colonies are ticked by default (not saved).</summary>
    private readonly HashSet<PlanetId> _excluded = [];

    public ShipyardViewModel(IUniverseContext context, ShipyardService shipyard, OwnPlanetService ownPlanets, IDialogService dialogs)
    {
        _context = context;
        _shipyard = shipyard;
        _ownPlanets = ownPlanets;
        _dialogs = dialogs;

        _context.CurrentChanged += (_, _) =>
        {
            _excluded.Clear();
            ShowPlanets();
        };
    }

    /// <summary>Ships first, then defenses (marked, as the game builds both in the shipyard).</summary>
    public static IReadOnlyList<UnitOption> Units { get; } = UnitCatalog.Units
        .OrderBy(u => u.Kind)
        .Select(u => new UnitOption(u.Id, u.Kind == UnitKind.Defense ? $"{u.Name} (defense)" : u.Name))
        .ToList();

    public ObservableCollection<PlanetSelectionItemViewModel> Planets { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OrderCommand))]
    private string _unitType = "BATTLESHIP";

    /// <summary>Committed when the field loses focus (it shows thousands separators), so it's checked on click.</summary>
    [ObservableProperty]
    private int _quantity = 10;

    [ObservableProperty]
    private string _planetsInfo = string.Empty;

    public override Task OnNavigatedToAsync()
    {
        ShowPlanets();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SelectAllPlanets() => SetAllPlanets(true);

    [RelayCommand]
    private void SelectNoPlanets() => SetAllPlanets(false);

    private void SetAllPlanets(bool selected)
    {
        foreach (var planet in Planets)
            planet.IsSelected = selected;
    }

    [RelayCommand]
    private async Task RefreshPlanetsAsync()
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        try
        {
            await _ownPlanets.RefreshAsync(client);
            ShowPlanets();
        }
        catch (Exception ex) when (ex is OgamexException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Refreshing planets failed: {ex.Message}");
        }
    }

    private bool CanOrder() => UnitCatalog.Get(UnitType) is not null;

    [RelayCommand(CanExecute = nameof(CanOrder))]
    private async Task OrderAsync()
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        if (Quantity < 1)
        {
            _dialogs.ShowError("Enter a quantity of at least 1.");
            return;
        }

        var selected = Planets.Where(p => p.IsSelected).Select(p => p.Planet.Id).ToHashSet();
        if (selected.Count == 0)
        {
            _dialogs.ShowError(Planets.Count == 0 ? "Refresh the planets and select some first." : "Select at least one planet.");
            return;
        }

        var name = UnitCatalog.NameOf(UnitType);
        AddLog($"Ordering {Quantity:N0} {name} on {selected.Count} planet(s)…");
        try
        {
            var results = await _shipyard.OrderAsync(client, selected, UnitType, Quantity, new Progress<ShipOrderResult>(r => AddLog(r.Message)));
            var queued = results.Count(r => r.Queued);
            AddLog($"Done: {queued * Quantity:N0} {name} ordered on {queued} of {results.Count} planet(s)");
        }
        catch (OgamexException ex)
        {
            AddLog($"Ordering failed: {ex.Message}");
            _dialogs.ShowError(ex is SessionExpiredException ? $"{ex.Message} Save a fresh session first." : $"Ordering failed: {ex.Message}");
        }
        finally
        {
            ShowPlanets(); // the order refreshes the planet list
        }
    }

    private void ShowPlanets()
    {
        var list = _context.Current is { } universe ? _ownPlanets.GetStored(universe.Name) : OwnPlanetList.Empty;
        Planets.Clear();
        foreach (var planet in list.Planets)
        {
            PlanetSelectionItemViewModel? item = null;
            item = new PlanetSelectionItemViewModel(planet, !_excluded.Contains(planet.Id), () =>
            {
                if (item!.IsSelected)
                    _excluded.Remove(planet.Id);
                else
                    _excluded.Add(planet.Id);
            });
            Planets.Add(item);
        }

        PlanetsInfo = list.RefreshedAt is { } at ? $"{Planets.Count} planets, refreshed {at:g}" : "Planet list not refreshed yet";
    }

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}

public sealed record UnitOption(string Id, string Name);
