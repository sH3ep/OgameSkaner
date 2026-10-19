using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Moons;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Moons;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>A moon building's target level on the Moons tab.</summary>
public sealed partial class MoonTargetItemViewModel(BuildingType building, int level, Action changed) : ObservableObject
{
    public BuildingType Building { get; } = building;

    public string Name => BuildingCatalog.Get(Building).DisplayName;

    [ObservableProperty]
    private int _level = level;

    partial void OnLevelChanged(int value) => changed();
}

/// <summary>
/// The Builder's Moons tab: the moons to build on (simulation or a fixed schema), the transports from each moon's planet
/// (manual and automatic, never more than the next hours need) and creating moons on several planets. Settings are
/// saved per universe on every change.
/// </summary>
public sealed partial class MoonsViewModel : ObservableObject
{
    private readonly IUniverseContext _context;
    private readonly MoonRunner _runner;
    private readonly OwnPlanetService _ownPlanets;
    private readonly IMoonSettingsStore _store;
    private readonly IDialogService _dialogs;
    private string? _loadedUniverse;
    private bool _loading;

    /// <summary>Ticked moons, including ticked ones missing from the list (e.g. before a refresh).</summary>
    private HashSet<string> _moonIds = [];

    public MoonsViewModel(IUniverseContext context, MoonRunner runner, OwnPlanetService ownPlanets, IMoonSettingsStore store, IDialogService dialogs,
        RunningModules running)
    {
        running.Track(typeof(BuilderViewModel), RunCommand);
        _context = context;
        _runner = runner;
        _ownPlanets = ownPlanets;
        _store = store;
        _dialogs = dialogs;

        _context.CurrentChanged += (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            Load();
        };
        RunCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunCommand.IsRunning))
                NotifyActions();
        };
    }

    public ObservableCollection<PlanetSelectionItemViewModel> Moons { get; } = [];

    /// <summary>Planets without a moon, to create one on (ticks aren't saved).</summary>
    public ObservableCollection<PlanetSelectionItemViewModel> PlanetsWithoutMoon { get; } = [];

    public ObservableCollection<MoonTargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<SchemaStep> SchemaSteps { get; } = [];

    public IReadOnlyList<BuildingDefinition> MoonBuildings { get; } = BuildingCatalog.MoonBuildings.Select(BuildingCatalog.Get).ToList();

    public ObservableCollection<CargoShipOptionViewModel> CargoShips { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSchema))]
    private bool _isSimulation = true;

    public bool IsSchema
    {
        get => !IsSimulation;
        set => IsSimulation = !value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddStepCommand))]
    private BuildingDefinition? _newStepBuilding;

    [ObservableProperty]
    private int _newStepLevel = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveStepCommand), nameof(MoveStepUpCommand), nameof(MoveStepDownCommand))]
    private SchemaStep? _selectedStep;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveCargoShipUpCommand), nameof(MoveCargoShipDownCommand))]
    private CargoShipOptionViewModel? _selectedCargoShip;

    [ObservableProperty]
    private double _horizonHours;

    [ObservableProperty]
    private bool _autoTransport;

    [ObservableProperty]
    private int _transportSpeedPercent;

    [ObservableProperty]
    private int _checkIntervalMinutes;

    [ObservableProperty]
    private int _maxServerQueueDepth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateCostText))]
    private int _createDiameter;

    /// <summary>Do everything except queuing, sending and creating (not saved).</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TransportNowCommand), nameof(CreateMoonsCommand), nameof(RefreshCommand))]
    private bool _isBusy;

    /// <summary>What a moon of the chosen size costs and needs, and the total for the ticked planets.</summary>
    public string CreateCostText
    {
        get
        {
            var diameter = Math.Clamp(CreateDiameter, MoonFormation.MinDiameter, MoonFormation.MaxDiameter);
            var cost = MoonFormation.MetalCost(diameter);
            var graviton = MoonFormation.GravitonNeeded(diameter);
            var ticked = PlanetsWithoutMoon.Count(p => p.IsSelected);
            return $"{cost:N0} metal per moon" + (graviton > 0 ? $", needs Graviton Research {graviton}" : "")
                   + (ticked > 0 ? $"; {ticked} planet(s): {cost * ticked:N0} metal" : "");
        }
    }

    /// <summary>Loads the universe's settings and lists when shown (the Builder page calls it).</summary>
    public void OnShown()
    {
        if (_loadedUniverse != _context.Current?.Name)
            Load();
        else
            ShowLists();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(StatusText) or nameof(IsBusy) or nameof(TestMode) or nameof(CreateCostText) or nameof(IsSchema)
                or nameof(NewStepBuilding) or nameof(NewStepLevel) or nameof(SelectedStep) or nameof(SelectedCargoShip):
                break;
            default:
                Save();
                break;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var settings = CurrentSettings();
        if (settings.MoonIds.Count == 0)
        {
            _dialogs.ShowError(Moons.Count == 0 ? "Refresh the moons (or create one) and tick them first." : "Tick at least one moon.");
            return;
        }

        StatusText = settings.TestMode ? "Running in test mode (nothing is queued or sent)…" : "Running…";
        AddLog($"Moon builder started: {settings.Mode}, {settings.MoonIds.Count} moon(s), transports {(settings.AutoTransport ? $"for {settings.Horizon.TotalHours:0.#} h" : "manual only")}.");
        var result = await _runner.RunAsync(client, settings, Progress(), cancellationToken);

        StatusText = result.Outcome == MoonOutcome.Cancelled
            ? $"Stopped: {result.Queued} level(s) queued, {result.Transports} transport(s)."
            : $"Failed: {result.Error}";
        AddLog(StatusText);
        ShowLists();
        if (result.Outcome == MoonOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "The moon builder failed.");
    }

    /// <summary>Starts the running loop's next cycle now instead of at its "Next check" time.</summary>
    [RelayCommand]
    private void CheckNow() => _runner.CheckNow();

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task TransportNowAsync() =>
        ActAsync(async client =>
        {
            var settings = CurrentSettings();
            if (settings.MoonIds.Count == 0)
            {
                AddLog("Tick at least one moon.");
                return;
            }

            var sent = await _runner.TransportNowAsync(client, settings, Progress());
            AddLog($"{sent} transport(s) sent.");
        });

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CreateMoonsAsync() =>
        ActAsync(async client =>
        {
            var planets = PlanetsWithoutMoon.Where(p => p.IsSelected).Select(p => p.Planet).ToList();
            if (planets.Count == 0)
            {
                AddLog("Tick the planets to create moons on.");
                return;
            }

            var diameter = Math.Clamp(CreateDiameter, MoonFormation.MinDiameter, MoonFormation.MaxDiameter);
            var cost = MoonFormation.MetalCost(diameter);
            if (!TestMode && !_dialogs.Confirm(
                    $"Create a {diameter:N0} km moon on {planets.Count} planet(s) for {cost:N0} metal each ({cost * planets.Count:N0} in all)?\n\n"
                    + string.Join("\n", planets.Select(p => p.ToString()))))
                return;

            var results = await _runner.CreateMoonsAsync(client, planets, diameter, TestMode, Progress());
            AddLog($"{results.Count(r => r.Moon is not null)} moon(s) created, {results.Count(r => r.Error is not null)} refused.");
            ShowLists();
        });

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshAsync() =>
        ActAsync(async client =>
        {
            var refresh = await _ownPlanets.RefreshAsync(client);
            ShowLists(refresh.List);
            AddLog($"{refresh.List.Moons.Count} moon(s), {refresh.List.Planets.Count} planet(s).");
        });

    private bool CanAct() => !IsBusy;

    [RelayCommand]
    private void SelectAllMoons() => SetAllMoons(true);

    [RelayCommand]
    private void SelectNoMoons() => SetAllMoons(false);

    [RelayCommand(CanExecute = nameof(CanAddStep))]
    private void AddStep()
    {
        if (NewStepBuilding is not { } building)
            return;
        SchemaSteps.Add(new SchemaStep(building.Type, Math.Max(1, NewStepLevel)));
        NewStepLevel++;
        Save();
    }

    private bool CanAddStep() => NewStepBuilding is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedStep))]
    private void RemoveStep()
    {
        if (SelectedStep is { } step)
            SchemaSteps.Remove(step);
        Save();
    }

    private bool HasSelectedStep() => SelectedStep is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedStep))]
    private void MoveStepUp() => MoveStep(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedStep))]
    private void MoveStepDown() => MoveStep(1);

    private void MoveStep(int by)
    {
        if (SelectedStep is not { } step)
            return;
        var index = SchemaSteps.IndexOf(step);
        var target = index + by;
        if (index < 0 || target < 0 || target >= SchemaSteps.Count)
            return;
        SchemaSteps.Move(index, target);
        Save();
    }

    [RelayCommand(CanExecute = nameof(CanMoveCargoShipUp))]
    private void MoveCargoShipUp() => MoveCargoShip(-1);

    private bool CanMoveCargoShipUp() => SelectedCargoShip is { } ship && CargoShips.IndexOf(ship) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveCargoShipDown))]
    private void MoveCargoShipDown() => MoveCargoShip(1);

    private bool CanMoveCargoShipDown() => SelectedCargoShip is { } ship && CargoShips.IndexOf(ship) is var i && i >= 0 && i < CargoShips.Count - 1;

    private void MoveCargoShip(int by)
    {
        if (SelectedCargoShip is not { } ship)
            return;
        var index = CargoShips.IndexOf(ship);
        CargoShips.Move(index, index + by);
        MoveCargoShipUpCommand.NotifyCanExecuteChanged();
        MoveCargoShipDownCommand.NotifyCanExecuteChanged();
        Save();
    }

    private async Task ActAsync(Func<IOgamexClient, Task> action)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        IsBusy = true;
        try
        {
            await action(client);
        }
        catch (Exception ex) when (ex is OgamexException or IOException or UnauthorizedAccessException)
        {
            AddLog(ex.Message);
            if (ex is SessionExpiredException)
                _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetAllMoons(bool selected)
    {
        _loading = true;
        try
        {
            foreach (var moon in Moons)
                moon.IsSelected = selected;
        }
        finally
        {
            _loading = false;
        }

        OnMoonSelectionChanged();
    }

    private void OnMoonSelectionChanged()
    {
        if (_loading)
            return;
        foreach (var moon in Moons)
        {
            if (moon.IsSelected)
                _moonIds.Add(moon.Planet.Id.Value);
            else
                _moonIds.Remove(moon.Planet.Id.Value);
        }

        Save();
    }

    /// <summary>The stored moons (new ones unticked) and the planets without a moon.</summary>
    private void ShowLists(OwnPlanetList? list = null)
    {
        list ??= _loadedUniverse is null ? OwnPlanetList.Empty : _ownPlanets.GetStored(_loadedUniverse);
        var wasLoading = _loading;
        _loading = true;
        try
        {
            Moons.Clear();
            foreach (var moon in list.Moons)
                Moons.Add(new PlanetSelectionItemViewModel(moon, _moonIds.Contains(moon.Id.Value), OnMoonSelectionChanged));

            var ticked = PlanetsWithoutMoon.Where(p => p.IsSelected).Select(p => p.Planet.Id).ToHashSet();
            var withMoon = list.Moons.Select(m => m.Coordinates).ToHashSet();
            PlanetsWithoutMoon.Clear();
            foreach (var planet in list.Planets.Where(p => !withMoon.Contains(p.Coordinates)))
                PlanetsWithoutMoon.Add(new PlanetSelectionItemViewModel(planet, ticked.Contains(planet.Id), () => OnPropertyChanged(nameof(CreateCostText))));
        }
        finally
        {
            _loading = wasLoading;
        }

        OnPropertyChanged(nameof(CreateCostText));
    }

    private void Load()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            MoonSettings settings;
            try
            {
                settings = _loadedUniverse is null ? MoonSettings.Default : _store.Load(_loadedUniverse);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                AddLog($"Loading the moon settings failed: {ex.Message}");
                settings = MoonSettings.Default;
            }

            _moonIds = settings.MoonIds.ToHashSet();
            IsSimulation = settings.Mode == MoonBuildMode.Simulation;
            Targets.Clear();
            foreach (var building in BuildingCatalog.MoonBuildings)
                Targets.Add(new MoonTargetItemViewModel(building, settings.TargetOf(building), Save));
            SchemaSteps.Clear();
            foreach (var step in settings.SchemaSteps)
                SchemaSteps.Add(step);
            CargoShips.Clear();
            var priority = settings.CargoShipTypes;
            foreach (var unit in priority.Select(UnitCatalog.Get).OfType<UnitInfo>().Concat(UnitCatalog.CargoShips.Where(u => !priority.Contains(u.Id))))
                CargoShips.Add(new CargoShipOptionViewModel(unit, priority.Contains(unit.Id), Save));
            HorizonHours = settings.HorizonHours;
            AutoTransport = settings.AutoTransport;
            TransportSpeedPercent = settings.TransportSpeedPercent;
            CheckIntervalMinutes = settings.CheckIntervalMinutes;
            MaxServerQueueDepth = settings.MaxServerQueueDepth;
            CreateDiameter = settings.CreateDiameter;
            NewStepBuilding ??= MoonBuildings[0];
            ShowLists();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save()
    {
        if (_loading || _loadedUniverse is null)
            return;
        try
        {
            _store.Save(_loadedUniverse, CurrentSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the moon settings failed: {ex.Message}");
        }
    }

    private MoonSettings CurrentSettings() => new()
    {
        MoonIds = _moonIds.Order(StringComparer.Ordinal).ToList(),
        Mode = IsSimulation ? MoonBuildMode.Simulation : MoonBuildMode.Schema,
        Targets = Targets.Select(t => new SchemaStep(t.Building, Math.Max(0, t.Level))).ToList(),
        SchemaSteps = SchemaSteps.ToList(),
        CargoShipTypes = CargoShips.Where(c => c.IsSelected).Select(c => c.Id).ToList(),
        HorizonHours = Math.Clamp(HorizonHours, 0.5, 48),
        AutoTransport = AutoTransport,
        TransportSpeedPercent = Math.Clamp(TransportSpeedPercent, 10, 100),
        CheckIntervalMinutes = Math.Max(1, CheckIntervalMinutes),
        MaxServerQueueDepth = Math.Clamp(MaxServerQueueDepth, 1, 15),
        CreateDiameter = Math.Clamp(CreateDiameter, MoonFormation.MinDiameter, MoonFormation.MaxDiameter),
        TestMode = TestMode
    };

    private void NotifyActions()
    {
        TransportNowCommand.NotifyCanExecuteChanged();
        CreateMoonsCommand.NotifyCanExecuteChanged();
    }

    private IProgress<MoonProgress> Progress() => new Progress<MoonProgress>(p => AddLog(p.Message));

    /// <summary>The automation runs for days, so old lines are dropped.</summary>
    private const int MaxLogLines = 1_000;

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:T}  {message}");
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(Log.Count - 1);
    }
}
