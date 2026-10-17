using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// The inactive farmer: raidable inactives from the galaxy database with their newest espionage report, the raid
/// settings (saved per universe on every change), a continuous run and manual spy/attack actions.
/// </summary>
public sealed partial class RaidsViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly RaidRunner _runner;
    private readonly IRaidStore _store;
    private readonly IDialogService _dialogs;
    private string? _loadedUniverse;
    private bool _loading;
    private IReadOnlyList<RaidTarget> _targets = [];

    public RaidsViewModel(IUniverseContext context, RaidRunner runner, IRaidStore store, IDialogService dialogs)
    {
        _context = context;
        _runner = runner;
        _store = store;
        _dialogs = dialogs;

        _context.CurrentChanged += (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            LoadSettings();
            _ = RefreshTargetsAsync();
        };

        RunCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunCommand.IsRunning))
                NotifyManualCommands();
        };
    }

    public static IReadOnlyList<UnitInfo> CargoShips => UnitCatalog.CargoShips;

    public ObservableCollection<RaidTargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private int _fleetSlotUsePercent;

    [ObservableProperty]
    private long _maxDefensePoints;

    [ObservableProperty]
    private long _maxFleetPoints;

    [ObservableProperty]
    private long _minLoot;

    [ObservableProperty]
    private int _lootPercent;

    [ObservableProperty]
    private string _cargoShipType = "HEAVY_CARGO";

    [ObservableProperty]
    private int _cargoMarginPercent;

    [ObservableProperty]
    private int _probeCount;

    [ObservableProperty]
    private int _reportMaxAgeMinutes;

    [ObservableProperty]
    private int _speedPercentage;

    [ObservableProperty]
    private int _maxRank;

    [ObservableProperty]
    private bool _useNearestPlanet;

    /// <summary>Show only targets that need a spy, are waiting for one or are ready.</summary>
    [ObservableProperty]
    private bool _hideRejected;

    /// <summary>Do everything except sending fleets (not saved).</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string? _targetsInfo;

    [ObservableProperty]
    private string? _defenseRankingInfo;

    // Grid filters (empty = no limit); they only change what the grid shows and aren't saved.
    [ObservableProperty]
    private int? _galaxyFrom;

    [ObservableProperty]
    private int? _galaxyTo;

    [ObservableProperty]
    private int? _systemFrom;

    [ObservableProperty]
    private int? _systemTo;

    [ObservableProperty]
    private long? _maxRankingDefensePoints;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadReportsCommand), nameof(SpySelectedCommand), nameof(AttackSelectedCommand))]
    private bool _isBusy;

    public override async Task OnNavigatedToAsync()
    {
        if (_loadedUniverse != _context.Current?.Name)
            LoadSettings();
        await RefreshTargetsAsync();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(HideRejected) or nameof(GalaxyFrom) or nameof(GalaxyTo) or nameof(SystemFrom) or nameof(SystemTo) or nameof(MaxRankingDefensePoints):
                ShowTargets();
                break;
            case nameof(TargetsInfo) or nameof(StatusText) or nameof(IsBusy) or nameof(DefenseRankingInfo):
                break;
            default:
                if (!_loading && _loadedUniverse is not null)
                {
                    SaveSettings();
                    _ = RefreshTargetsAsync();
                }
                break;
        }
    }

    [RelayCommand]
    private async Task RefreshTargetsAsync()
    {
        if (_context.Current is not { } universe)
        {
            _targets = [];
            ShowTargets();
            return;
        }

        try
        {
            _targets = await _runner.GetTargetsAsync(universe.Name, CurrentSettings());
            DefenseRankingInfo = _runner.DefenseRankingReadAt(universe.Name) is { } readAt
                ? $"Read every player's defense points from the game's ranking (34 pages on genesis; the raider reads it again when it is {RaidRunner.RankingMaxAge.TotalHours:0} hours old). Last read {readAt.ToLocalTime():g}."
                : "Read every player's defense points from the game's ranking (34 pages on genesis). Not read yet.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AddLog($"Loading raid data failed: {ex.Message}");
            _targets = [];
        }

        ShowTargets();
    }

    [RelayCommand]
    private void SelectReady()
    {
        foreach (var item in Targets)
            item.IsSelected = item.State == RaidTargetState.Ready;
    }

    [RelayCommand]
    private void SelectNeedingSpy()
    {
        foreach (var item in Targets)
            item.IsSelected = item.State == RaidTargetState.NeedsSpy;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ReadReportsAsync() =>
        ActAsync(client => _runner.ReadReportsAsync(client, CurrentSettings(), Progress(), all: true), n => $"{n} report(s) loaded from the messages");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ReadDefenseRankingAsync() =>
        ActAsync(client => _runner.ReadDefenseRankingAsync(client, Progress()), n => $"Defense ranking read: {n:N0} players");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task SpySelectedAsync() =>
        ActAsync(client => _runner.SpyAsync(client, Selected(), CurrentSettings(), Progress()), n => $"{n} spy mission(s) sent");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task AttackSelectedAsync()
    {
        var selected = Selected();
        if (selected.Count > 0 && !TestMode && !_dialogs.Confirm($"Attack {selected.Count} selected planet(s) with {UnitCatalog.NameOf(CargoShipType)}?"))
            return Task.CompletedTask;
        return ActAsync(client => _runner.AttackAsync(client, selected, CurrentSettings(), Progress()), n => $"{n} attack(s) sent");
    }

    private bool CanAct() => !IsBusy && !RunCommand.IsRunning;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        StatusText = TestMode ? "Running in test mode (nothing is sent)…" : "Running…";
        AddLog(TestMode ? "Raider started in test mode" : "Raider started");
        var result = await _runner.RunAsync(client, CurrentSettings(), Progress(refreshOnCycle: true), cancellationToken);

        StatusText = result.Outcome == RaidOutcome.Cancelled
            ? $"Stopped after {result.Attacks} attacks and {result.Spies} spy missions."
            : $"Failed after {result.Attacks} attacks and {result.Spies} spy missions.";
        AddLog(StatusText);
        await RefreshTargetsAsync();

        if (result.Outcome == RaidOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "The raider failed.");
    }

    private async Task ActAsync(Func<IOgamexClient, Task<int>> action, Func<int, string> describe)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        IsBusy = true;
        try
        {
            AddLog(describe(await action(client)));
        }
        catch (OgamexException ex)
        {
            AddLog(ex.Message);
            if (ex is SessionExpiredException)
                _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshTargetsAsync();
    }

    private IProgress<RaidProgress> Progress(bool refreshOnCycle = false) => new Progress<RaidProgress>(p =>
    {
        AddLog(p.Message);
        if (refreshOnCycle && p.Message.StartsWith("Next check", StringComparison.Ordinal))
            _ = RefreshTargetsAsync();
    });

    private List<RaidTarget> Selected() => Targets.Where(t => t.IsSelected).Select(t => t.Target).ToList();

    private void ShowTargets()
    {
        var selected = Targets.Where(t => t.IsSelected).Select(t => t.Target.Planet.Coordinates).ToHashSet();
        var now = DateTimeOffset.Now;
        var filter = new RaidTargetFilter(GalaxyFrom, GalaxyTo, SystemFrom, SystemTo, MaxRankingDefensePoints);
        var visible = (HideRejected
            ? _targets.Where(t => t.State is RaidTargetState.Ready or RaidTargetState.NeedsSpy or RaidTargetState.SpyPending or RaidTargetState.UnderAttack)
            : _targets).Where(filter.Matches).ToList();

        Targets.Clear();
        foreach (var target in visible)
            Targets.Add(new RaidTargetItemViewModel(target, now) { IsSelected = selected.Contains(target.Planet.Coordinates) });

        var ready = _targets.Count(t => t.State == RaidTargetState.Ready);
        var spy = _targets.Count(t => t.State == RaidTargetState.NeedsSpy);
        TargetsInfo = _targets.Count == 0
            ? "No raidable inactive planets in the galaxy database. Scan the galaxy first (the scan stores each player's status and rank)."
            : $"{_targets.Count} inactive planets: {ready} ready, {spy} to spy, {_targets.Count - ready - spy} other"
              + (visible.Count < _targets.Count ? $"; {visible.Count} shown" : "");
    }

    private void NotifyManualCommands()
    {
        ReadReportsCommand.NotifyCanExecuteChanged();
        ReadDefenseRankingCommand.NotifyCanExecuteChanged();
        SpySelectedCommand.NotifyCanExecuteChanged();
        AttackSelectedCommand.NotifyCanExecuteChanged();
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            var settings = _loadedUniverse is null ? RaidSettings.Default : _store.LoadSettings(_loadedUniverse);
            FleetSlotUsePercent = settings.FleetSlotUsePercent;
            MaxDefensePoints = settings.MaxDefensePoints;
            MaxFleetPoints = settings.MaxFleetPoints;
            MinLoot = settings.MinLoot;
            LootPercent = settings.LootPercent;
            CargoShipType = settings.CargoShipType;
            CargoMarginPercent = settings.CargoMarginPercent;
            ProbeCount = settings.ProbeCount;
            ReportMaxAgeMinutes = settings.ReportMaxAgeMinutes;
            SpeedPercentage = settings.SpeedPercentage;
            MaxRank = settings.MaxRank;
            UseNearestPlanet = settings.UseNearestPlanet;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSettings()
    {
        try
        {
            _store.SaveSettings(_loadedUniverse!, CurrentSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving raid settings failed: {ex.Message}");
        }
    }

    private RaidSettings CurrentSettings() => new()
    {
        FleetSlotUsePercent = Math.Clamp(FleetSlotUsePercent, 0, 100),
        MaxDefensePoints = Math.Max(0, MaxDefensePoints),
        MaxFleetPoints = Math.Max(0, MaxFleetPoints),
        MinLoot = Math.Max(0, MinLoot),
        LootPercent = Math.Clamp(LootPercent, 1, 100),
        CargoShipType = CargoShipType,
        CargoMarginPercent = Math.Max(0, CargoMarginPercent),
        ProbeCount = Math.Max(1, ProbeCount),
        ReportMaxAgeMinutes = Math.Max(1, ReportMaxAgeMinutes),
        SpeedPercentage = Math.Clamp(SpeedPercentage, 1, 100),
        MaxRank = Math.Max(0, MaxRank),
        UseNearestPlanet = UseNearestPlanet,
        TestMode = TestMode
    };

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}
