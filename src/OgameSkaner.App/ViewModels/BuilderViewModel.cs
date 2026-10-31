using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>Build automation: edits the strategy (saved per universe on every change) and runs it.</summary>
public sealed partial class BuilderViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly SimulatedBuildRunner _simulated;
    private readonly OwnPlanetService _ownPlanets;
    private readonly IActionQueues _queues;
    private readonly IBuildSettingsStore _store;
    private readonly IDialogService _dialogs;
    private readonly AutomationActivity _activity;
    private string? _loadedUniverse;
    private HashSet<string> _excludedPlanetIds = [];
    private bool _loading;
    private bool _restoringViewedPlanet;

    public BuilderViewModel(
        IUniverseContext context,
        SimulatedBuildRunner simulated,
        OwnPlanetService ownPlanets,
        IActionQueues queues,
        IBuildSettingsStore store,
        IDialogService dialogs,
        AutomationActivity activity,
        RunningModules running,
        MoonsViewModel moons)
    {
        running.Track(this, RunCommand);
        Moons = moons;
        _activity = activity;
        _context = context;
        _simulated = simulated;
        _ownPlanets = ownPlanets;
        _queues = queues;
        _store = store;
        _dialogs = dialogs;

        // A running build belongs to the universe it was started for.
        _context.CurrentChanged += (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            LoadSettings();
        };

        RunCommand.PropertyChanged += (_, e) =>
        {
            // Levels change during a run; show the fresh ones afterwards.
            if (e.PropertyName == nameof(RunCommand.IsRunning) && !RunCommand.IsRunning && ViewedPlanet is not null)
                _ = ShowViewedPlanetAsync();
        };
    }

    /// <summary>The Moons tab.</summary>
    public MoonsViewModel Moons { get; }

    public ObservableCollection<BuildingRuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    /// <summary>The planets with a checkbox each; unticked ones are left alone by every automation run.</summary>
    public ObservableCollection<PlanetSelectionItemViewModel> Planets { get; } = [];

    [ObservableProperty]
    private int _referenceLevel;

    [ObservableProperty]
    private int _solarMaxLevel;

    [ObservableProperty]
    private int _storageFillPercent;

    [ObservableProperty]
    private int _mineBalancePercent;

    [ObservableProperty]
    private int _earlyRoboticsLevel;

    [ObservableProperty]
    private int _earlyRoboticsMineLevel;

    /// <summary>Orders simulated per plan of one planet (Simulated mode).</summary>
    [ObservableProperty]
    private int _simulationsPerPlan;

    [ObservableProperty]
    private bool _useSolarSatellites;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulatedMode))]
    private BuilderMode _mode;

    public bool IsSimulatedMode => Mode == BuilderMode.Simulated;

    /// <summary>Resource sharing: transports from planets with a surplus to planets waiting for stock.</summary>
    [ObservableProperty]
    private bool _shareResources;

    [ObservableProperty]
    private int _shareSpeedPercent;

    [ObservableProperty]
    private int _shareNextItems;

    [ObservableProperty]
    private long _shareMinTransportAmount;

    [ObservableProperty]
    private int _shareMaxSlots;

    [ObservableProperty]
    private long _shareKeepDeuterium;

    [ObservableProperty]
    private double _shareMinHoursSaved;

    /// <summary>Plan and log the transports without sending them (not saved).</summary>
    [ObservableProperty]
    private bool _shareTestMode;

    /// <summary>The cargo ships the sharing may send; the order is the priority.</summary>
    public ObservableCollection<CargoShipOptionViewModel> ShareCargoShips { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveShareCargoShipUpCommand), nameof(MoveShareCargoShipDownCommand))]
    private CargoShipOptionViewModel? _selectedShareCargoShip;

    public static IReadOnlyList<BuilderModeOption> Modes { get; } =
    [
        new(BuilderMode.Simulated, "Simulated (fastest order)"),
        new(BuilderMode.ByPrice, "By price (cheapest first)"),
        new(BuilderMode.ByLevel, "By level (keep the offsets)")
    ];


    [ObservableProperty]
    private string? _planetsInfo;

    /// <summary>The planet whose levels are shown in the Actual column (selected in the planet list).</summary>
    [ObservableProperty]
    private PlanetSelectionItemViewModel? _viewedPlanet;

    [ObservableProperty]
    private string? _viewedPlanetInfo;

    [ObservableProperty]
    private string? _statusText;

    public override Task OnNavigatedToAsync()
    {
        if (_context.Current?.Name != _loadedUniverse)
            LoadSettings();
        Moons.OnShown();
        return Task.CompletedTask;
    }

    partial void OnReferenceLevelChanged(int value)
    {
        foreach (var rule in Rules)
            rule.UpdateReferenceLevel(value);
        SaveSettings();
    }

    partial void OnSolarMaxLevelChanged(int value) => SaveSettings();

    partial void OnStorageFillPercentChanged(int value) => SaveSettings();

    partial void OnMineBalancePercentChanged(int value) => SaveSettings();

    partial void OnEarlyRoboticsLevelChanged(int value) => SaveSettings();

    partial void OnEarlyRoboticsMineLevelChanged(int value) => SaveSettings();

    partial void OnSimulationsPerPlanChanged(int value) => SaveSettings();

    partial void OnUseSolarSatellitesChanged(bool value) => SaveSettings();

    partial void OnModeChanged(BuilderMode value) => SaveSettings();

    partial void OnShareResourcesChanged(bool value) => SaveSettings();

    partial void OnShareSpeedPercentChanged(int value) => SaveSettings();

    partial void OnShareNextItemsChanged(int value) => SaveSettings();

    partial void OnShareMinTransportAmountChanged(long value) => SaveSettings();

    partial void OnShareMaxSlotsChanged(int value) => SaveSettings();

    partial void OnShareKeepDeuteriumChanged(long value) => SaveSettings();

    partial void OnShareMinHoursSavedChanged(double value) => SaveSettings();

    [RelayCommand(CanExecute = nameof(CanMoveShareCargoShipUp))]
    private void MoveShareCargoShipUp() => MoveShareCargoShip(-1);

    private bool CanMoveShareCargoShipUp() => SelectedShareCargoShip is { } ship && ShareCargoShips.IndexOf(ship) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveShareCargoShipDown))]
    private void MoveShareCargoShipDown() => MoveShareCargoShip(1);

    private bool CanMoveShareCargoShipDown() =>
        SelectedShareCargoShip is { } ship && ShareCargoShips.IndexOf(ship) is var i && i >= 0 && i < ShareCargoShips.Count - 1;

    private void MoveShareCargoShip(int by)
    {
        if (SelectedShareCargoShip is not { } ship)
            return;
        var index = ShareCargoShips.IndexOf(ship);
        ShareCargoShips.Move(index, index + by);
        MoveShareCargoShipUpCommand.NotifyCanExecuteChanged();
        MoveShareCargoShipDownCommand.NotifyCanExecuteChanged();
        SaveSettings();
    }

    partial void OnViewedPlanetChanged(PlanetSelectionItemViewModel? value)
    {
        if (!_restoringViewedPlanet)
            _ = ShowViewedPlanetAsync();
    }

    [RelayCommand]
    private Task RefreshViewedPlanetAsync() => ShowViewedPlanetAsync();

    /// <summary>Reads the viewed planet and fills the Actual column and the summary line.</summary>
    private async Task ShowViewedPlanetAsync()
    {
        ClearActual();
        if (ViewedPlanet?.Planet is not { } planet || _context.Client is not { } client)
            return;

        if (RunCommand.IsRunning)
        {
            ViewedPlanetInfo = $"{planet.Name}: levels are shown when the run has finished.";
            return;
        }

        ViewedPlanetInfo = $"Reading {planet}…";
        try
        {
            var state = await _queues.For(client).RunAsync("Builder", $"Read {planet}", ct => _ownPlanets.InspectAsync(client, planet.Id, ct), ActionPriority.User);
            if (ViewedPlanet?.Planet != planet)
                return; // another planet was selected meanwhile

            foreach (var rule in Rules)
                rule.Actual = DescribeLevel(state, rule.Building);
            ViewedPlanetInfo = DescribePlanet(state);
        }
        catch (OgamexException ex)
        {
            ViewedPlanetInfo = $"Reading {planet} failed: {ex.Message}";
        }
    }

    private void ClearActual()
    {
        foreach (var rule in Rules)
            rule.Actual = null;
        ViewedPlanetInfo = null;
    }

    private static string DescribeLevel(PlanetBuildState state, BuildingType building)
    {
        if (state.Locked.Contains(building))
            return "locked";

        var level = state.LevelOf(building);
        var queued = state.Queue.Where(q => q.Building == building).Select(q => q.Level).DefaultIfEmpty(level).Max();
        return queued > level ? $"{level} → {queued}" : level.ToString();
    }

    private string DescribePlanet(PlanetBuildState state)
    {
        static string Fill(long amount, long capacity) => capacity > 0 ? $"{100 * amount / capacity}%" : "?";

        var capacity = state.StorageCapacity ?? default;
        return string.Join("   ",
            $"{state.Planet}:",
            $"queue {state.Queue.Count}",
            $"energy {state.EnergyBalance:+#,0;-#,0;0}",
            $"free fields {state.FreeFields?.ToString() ?? "?"}",
            $"solar plant {DescribeLevel(state, BuildingType.SolarPlant)}",
            $"storages {DescribeLevel(state, BuildingType.MetalStorage)} / {DescribeLevel(state, BuildingType.CrystalStorage)} / {DescribeLevel(state, BuildingType.DeuteriumTank)}"
                + $" (full {Fill(state.Resources.Metal, capacity.Metal)} / {Fill(state.Resources.Crystal, capacity.Crystal)} / {Fill(state.Resources.Deuterium, capacity.Deuterium)})",
            $"read {DateTime.Now:T}");
    }

    [RelayCommand]
    private void SelectAllPlanets() => SetAllPlanets(true);

    [RelayCommand]
    private void SelectNoPlanets() => SetAllPlanets(false);

    private void SetAllPlanets(bool selected)
    {
        _loading = true;
        try
        {
            foreach (var planet in Planets)
                planet.IsSelected = selected;
        }
        finally
        {
            _loading = false;
        }

        OnPlanetSelectionChanged();
    }

    private void OnPlanetSelectionChanged()
    {
        if (_loading)
            return;

        // Keep exclusions of planets that are not in the current list (e.g. before a refresh).
        foreach (var planet in Planets)
        {
            if (planet.IsSelected)
                _excludedPlanetIds.Remove(planet.Planet.Id.Value);
            else
                _excludedPlanetIds.Add(planet.Planet.Id.Value);
        }

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
            ShowPlanets((await _ownPlanets.RefreshAsync(client)).List);
            AddLog($"Planet list refreshed: {Planets.Count} planets");
        }
        catch (Exception ex) when (ex is OgamexException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Refreshing planets failed: {ex.Message}");
        }
    }

    /// <summary>Starts the running loop's next cycle now instead of at its "Next check" time.</summary>
    [RelayCommand]
    private void CheckNow() => _simulated.CheckNow();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var selected = Planets.Where(p => p.IsSelected).Select(p => p.Planet).ToList();
        if (selected.Count == 0)
        {
            _dialogs.ShowError(Planets.Count == 0 ? "Refresh the planets and select some first." : "Select at least one planet.");
            return;
        }

        var settings = CurrentSettings();
        var request = new BuildRequest(settings.ToStrategy(), selected.Select(p => p.Id).ToHashSet());
        var progress = new Progress<BuildProgress>(p =>
        {
            AddLog(p.Message);
            StatusText = $"Running… {p.TotalQueued} items queued";
        });

        if (!_activity.TryStart(ActivityName))
        {
            _dialogs.ShowError($"{_activity.Running} is running and feeds the same build queue. Stop it first.");
            return;
        }

        BuildRunResult result;
        try
        {
            AddLog($"Build started on {string.Join(", ", selected.Select(p => p.Name))}");
            result = await _simulated.RunAsync(client, request, settings.ToSimulatedOptions(), progress, cancellationToken);
        }
        finally
        {
            _activity.Stop(ActivityName);
        }

        ShowPlanets(_ownPlanets.GetStored(client.Universe.Name));

        StatusText = result.Outcome switch
        {
            BuildOutcome.Completed => $"Finished: {result.TotalQueued} items queued on {result.Planets.Count} planets.",
            BuildOutcome.Cancelled => $"Stopped after {result.TotalQueued} queued items.",
            _ => $"Failed after {result.TotalQueued} queued items."
        };
        AddLog(StatusText);

        if (result.Outcome == BuildOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "Build automation failed.");
    }

    private const string ActivityName = "The Builder";

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            var settings = _loadedUniverse is null ? BuildSettings.CreateDefault() : _store.Load(_loadedUniverse);

            ReferenceLevel = settings.ReferenceLevel;
            SolarMaxLevel = settings.SolarMaxLevel;
            StorageFillPercent = settings.StorageFillPercent;
            MineBalancePercent = settings.MineBalancePercent;
            EarlyRoboticsLevel = settings.EarlyRoboticsLevel;
            EarlyRoboticsMineLevel = settings.EarlyRoboticsMineLevel;
            SimulationsPerPlan = settings.SimulationsPerPlan;
            UseSolarSatellites = settings.UseSolarSatellites;
            Mode = settings.Mode;
            ShareResources = settings.ShareResources;
            ShareSpeedPercent = settings.ShareSpeedPercent;
            ShareNextItems = settings.ShareNextItems;
            ShareMinTransportAmount = settings.ShareMinTransportAmount;
            ShareMaxSlots = settings.ShareMaxSlots;
            ShareKeepDeuterium = settings.ShareKeepDeuterium;
            ShareMinHoursSaved = settings.ShareMinHoursSaved;
            ShareCargoShips.Clear();
            var priority = settings.ShareCargoShipTypes;
            foreach (var unit in priority.Select(UnitCatalog.Get).OfType<UnitInfo>().Concat(UnitCatalog.CargoShips.Where(u => !priority.Contains(u.Id))))
                ShareCargoShips.Add(new CargoShipOptionViewModel(unit, priority.Contains(unit.Id), SaveSettings));
            _excludedPlanetIds = settings.ExcludedPlanetIds.ToHashSet();
            ShowPlanets(_loadedUniverse is null ? OwnPlanetList.Empty : _ownPlanets.GetStored(_loadedUniverse));

            Rules.Clear();
            foreach (var rule in settings.Rules)
                Rules.Add(new BuildingRuleItemViewModel(rule, settings.ReferenceLevel, SaveSettings));
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowPlanets(OwnPlanetList list)
    {
        var viewedId = ViewedPlanet?.Planet.Id;
        Planets.Clear();
        foreach (var planet in list.Planets)
            Planets.Add(new PlanetSelectionItemViewModel(planet, !_excludedPlanetIds.Contains(planet.Id.Value), OnPlanetSelectionChanged));

        PlanetsInfo = list.RefreshedAt is { } at
            ? $"{Planets.Count} planets, refreshed {at:g}"
            : "Planet list not refreshed yet";

        // Keep the viewed planet without reading it again; the new row object only replaces the old one.
        _restoringViewedPlanet = true;
        try
        {
            ViewedPlanet = Planets.FirstOrDefault(p => p.Planet.Id == viewedId);
        }
        finally
        {
            _restoringViewedPlanet = false;
        }

        if (ViewedPlanet is null)
            ClearActual();
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
            AddLog($"Saving build settings failed: {ex.Message}");
        }
    }

    private BuildSettings CurrentSettings() => new()
    {
        ReferenceLevel = ReferenceLevel,
        SolarMaxLevel = SolarMaxLevel,
        StorageFillPercent = StorageFillPercent,
        MineBalancePercent = Math.Clamp(MineBalancePercent, 0, 100),
        EarlyRoboticsLevel = Math.Max(0, EarlyRoboticsLevel),
        EarlyRoboticsMineLevel = Math.Max(0, EarlyRoboticsMineLevel),
        Rules = Rules.Select(r => r.ToRule()).ToList(),
        SimulationsPerPlan = SimulationsPerPlan,
        UseSolarSatellites = UseSolarSatellites,
        Mode = Mode,
        ShareResources = ShareResources,
        ShareCargoShipTypes = ShareCargoShips.Where(c => c.IsSelected).Select(c => c.Id).ToList(),
        ShareSpeedPercent = Math.Clamp(ShareSpeedPercent, 10, 100),
        ShareNextItems = Math.Max(1, ShareNextItems),
        ShareMinTransportAmount = Math.Max(0, ShareMinTransportAmount),
        ShareMaxSlots = Math.Max(1, ShareMaxSlots),
        ShareKeepDeuterium = Math.Max(0, ShareKeepDeuterium),
        ShareMinHoursSaved = Math.Max(0, ShareMinHoursSaved),
        ShareTestMode = ShareTestMode,
        ExcludedPlanetIds = _excludedPlanetIds.Order(StringComparer.Ordinal).ToList()
    };

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}

public sealed record BuilderModeOption(BuilderMode Id, string Name);
