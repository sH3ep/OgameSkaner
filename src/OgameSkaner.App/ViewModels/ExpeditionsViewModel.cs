using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Expeditions;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Expeditions;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// Expeditions: the ship types that fly, the planets and moons that send them and how many each keeps in the air. The
/// continuous run sends whenever a slot and ships are free. Options are saved per universe on every change.
/// </summary>
public sealed partial class ExpeditionsViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly ExpeditionRunner _runner;
    private readonly OwnPlanetService _ownPlanets;
    private readonly IExpeditionSettingsStore _store;
    private readonly IDialogService _dialogs;
    private string? _loadedUniverse;
    private bool _loading;

    public ExpeditionsViewModel(
        IUniverseContext context, ExpeditionRunner runner, OwnPlanetService ownPlanets, IExpeditionSettingsStore store, IDialogService dialogs,
        RunningModules running)
    {
        running.Track(this, RunCommand);
        _context = context;
        _runner = runner;
        _ownPlanets = ownPlanets;
        _store = store;
        _dialogs = dialogs;

        _context.CurrentChanged += async (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            LoadSettings();
            await StartIfEnabledAsync();
        };
    }

    /// <summary>Expedition durations the game offers (1 to 12 hours).</summary>
    public static IReadOnlyList<int> HoldHourOptions { get; } = Enumerable.Range(1, 12).ToList();

    /// <summary>Expeditions a place can keep in the air (the game's expedition slots, at most 12 on genesis).</summary>
    public static IReadOnlyList<int> CountOptions { get; } = Enumerable.Range(1, 12).ToList();

    public ObservableCollection<CargoShipOptionViewModel> ShipTypes { get; } = [];

    public ObservableCollection<ExpeditionOriginItemViewModel> Origins { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private int _holdHours;

    [ObservableProperty]
    private int _speedPercent;

    [ObservableProperty]
    private int _keepFleetSlotsFree;

    [ObservableProperty]
    private int _checkIntervalMinutes;

    [ObservableProperty]
    private bool _autoStart;

    /// <summary>Do everything except sending fleets (not saved).</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string _planetsInfo = string.Empty;

    public override Task OnNavigatedToAsync()
    {
        if (_loadedUniverse != _context.Current?.Name)
            LoadSettings();
        return Task.CompletedTask;
    }

    /// <summary>Starts sending when <see cref="AutoStart"/> is on for the selected universe (app start, universe change).</summary>
    public async Task StartIfEnabledAsync()
    {
        if (_loadedUniverse != _context.Current?.Name)
            LoadSettings();
        if (!AutoStart || _context.Client is null)
            return;

        // A run of the previous universe may still be finishing its cancellation.
        if (RunCommand.ExecutionTask is { IsCompleted: false } running)
            await running;
        if (!RunCommand.IsRunning)
            await RunCommand.ExecuteAsync(null);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(StatusText) or nameof(TestMode) or nameof(PlanetsInfo)))
            SaveSettings();
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
            ShowOrigins(CurrentSettings());
        }
        catch (Exception ex) when (ex is OgamexException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Refreshing planets failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void UseAll() => SetUsed(true);

    [RelayCommand]
    private void UseNone() => SetUsed(false);

    [RelayCommand]
    private void SelectAllShips() => SetShipsSelected(true);

    [RelayCommand]
    private void SelectNoShips() => SetShipsSelected(false);

    /// <summary>Starts the running loop's next cycle now instead of at its "Next check" time.</summary>
    [RelayCommand]
    private void CheckNow() => _runner.CheckNow();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var settings = CurrentSettings();
        StatusText = settings.TestMode ? "Sending expeditions in test mode (no fleet is sent)…" : "Sending expeditions…";
        AddLog($"Expeditions started ({Describe(settings)}). Option changes apply on the next start.");
        var result = await _runner.RunAsync(client, settings, new Progress<ExpeditionProgress>(OnProgress), cancellationToken);

        StatusText = result.Outcome == ExpeditionOutcome.Cancelled
            ? $"Stopped. {result.Sent} expedition(s) sent."
            : $"Expeditions stopped: {result.Error}";
        AddLog(StatusText);
        if (result.Outcome == ExpeditionOutcome.Failed)
            _dialogs.ShowError($"Expeditions stopped: {result.Error}");
    }

    private void OnProgress(ExpeditionProgress progress)
    {
        if (progress.Overview is { } overview)
        {
            var flying = overview.Origins.ToDictionary(o => o.Place.Id.Value, o => o.Flying);
            foreach (var origin in Origins)
                origin.Flying = flying.GetValueOrDefault(origin.Id);
            if (overview.Expeditions is { } expeditions && overview.Fleets is { } fleets)
                StatusText = $"Running: expeditions {expeditions.Used}/{expeditions.Max}, fleets {fleets.Used}/{fleets.Max} at the last send.";
        }

        if (progress.Message.Length > 0)
            AddLog(progress.Message);
    }

    private void SetUsed(bool used)
    {
        _loading = true;
        try
        {
            foreach (var origin in Origins)
                origin.IsUsed = used;
        }
        finally
        {
            _loading = false;
        }

        SaveSettings();
    }

    private void SetShipsSelected(bool selected)
    {
        _loading = true;
        try
        {
            foreach (var ship in ShipTypes)
                ship.IsSelected = selected;
        }
        finally
        {
            _loading = false;
        }

        SaveSettings();
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            var settings = _loadedUniverse is null ? ExpeditionSettings.Default : _store.Load(_loadedUniverse);
            HoldHours = Math.Clamp((int)Math.Round(settings.HoldMinutes / 60.0), 1, 12);
            SpeedPercent = settings.SpeedPercent;
            KeepFleetSlotsFree = settings.KeepFleetSlotsFree;
            CheckIntervalMinutes = settings.CheckIntervalMinutes;
            AutoStart = settings.AutoStart;

            ShipTypes.Clear();
            foreach (var ship in UnitCatalog.FlyingShips)
                ShipTypes.Add(new CargoShipOptionViewModel(ship, settings.ShipTypes.Contains(ship.Id), SaveSettings));
            ShowOrigins(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AddLog($"Loading the expedition settings failed: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The stored planets, then the moons, with the counts of <paramref name="settings"/> (1 for the unused ones).</summary>
    private void ShowOrigins(ExpeditionSettings settings)
    {
        var loading = _loading;
        _loading = true;
        try
        {
            var list = _loadedUniverse is null ? OwnPlanetList.Empty : _ownPlanets.GetStored(_loadedUniverse);
            Origins.Clear();
            foreach (var (place, isMoon) in list.Planets.Select(p => (p, false)).Concat(list.Moons.Select(m => (m, true))))
            {
                var count = settings.CountFor(place.Id.Value);
                Origins.Add(new ExpeditionOriginItemViewModel(place, isMoon, count > 0, Math.Clamp(count, 1, CountOptions[^1]), SaveSettings));
            }

            PlanetsInfo = list.RefreshedAt is { } at
                ? $"{list.Planets.Count} planets, {list.Moons.Count} moons, refreshed {at:g}"
                : "Planet list not refreshed yet";
        }
        finally
        {
            _loading = loading;
        }
    }

    private void SaveSettings()
    {
        if (_loading || _loadedUniverse is null)
            return;
        try
        {
            _store.Save(_loadedUniverse, CurrentSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the expedition settings failed: {ex.Message}");
        }
    }

    private ExpeditionSettings CurrentSettings() => new()
    {
        ShipTypes = ShipTypes.Where(s => s.IsSelected).Select(s => s.Id).ToList(),
        Origins = Origins.Where(o => o.IsUsed && o.Count > 0).Select(o => new ExpeditionOrigin(o.Id, o.Count)).ToList(),
        HoldMinutes = Math.Clamp(HoldHours, 1, 12) * 60,
        SpeedPercent = Math.Clamp(SpeedPercent, 10, 100),
        KeepFleetSlotsFree = Math.Max(0, KeepFleetSlotsFree),
        CheckIntervalMinutes = Math.Max(1, CheckIntervalMinutes),
        AutoStart = AutoStart,
        TestMode = TestMode
    };

    private static string Describe(ExpeditionSettings settings) =>
        $"{settings.Origins.Sum(o => o.Count)} expedition(s) from {settings.Origins.Count} place(s), "
        + $"{settings.HoldMinutes / 60} h at {settings.SpeedPercent}%, {settings.KeepFleetSlotsFree} fleet slot(s) kept free";

    /// <summary>The run goes on for days and logs every check, so old lines are dropped.</summary>
    private const int MaxLogLines = 1_000;

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:T}  {message}");
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(Log.Count - 1);
    }
}
