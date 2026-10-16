using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Optimizer;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>Sent after a schema was added to the library outside the Schemas page.</summary>
public sealed record SchemaLibraryChangedMessage(Guid SchemaId);

/// <summary>
/// Build order optimizer: simulates the game for a setup (target mine levels on a fresh planet) and searches the
/// fastest order on all cores. The account's bonuses (research, officers, academy, premium, ranking, class) are read
/// from the game before every search. The best order per setup is kept and can be saved as a schema.
/// </summary>
public sealed partial class OptimizerViewModel : PageViewModel
{
    private readonly BuildOrderOptimizer _optimizer;
    private readonly IOptimizerResultStore _store;
    private readonly ISchemaStore _schemas;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly IUniverseContext _context;
    private AccountBonuses _bonuses = AccountBonuses.Calibration;
    private bool _loading;

    public OptimizerViewModel(
        BuildOrderOptimizer optimizer,
        IOptimizerResultStore store,
        ISchemaStore schemas,
        IDialogService dialogs,
        IMessenger messenger,
        IUniverseContext context)
    {
        _optimizer = optimizer;
        _store = store;
        _schemas = schemas;
        _dialogs = dialogs;
        _messenger = messenger;
        _context = context;
        Load();
    }

    public ObservableCollection<OptimizedOrder> SavedResults { get; } = [];

    public ObservableCollection<OptimizedStepItem> Steps { get; } = [];

    [ObservableProperty] private int _metalMine;
    [ObservableProperty] private int _crystalMine;
    [ObservableProperty] private int _deuteriumSynthesizer;
    [ObservableProperty] private int _planetSlot;
    [ObservableProperty] private int _minTemperature;
    [ObservableProperty] private int _maxTemperature;
    [ObservableProperty] private long _startMetal;
    [ObservableProperty] private long _startCrystal;
    [ObservableProperty] private long _startDeuterium;
    [ObservableProperty] private int _solarMaxLevel;
    [ObservableProperty] private int _roboticsMaxLevel;
    [ObservableProperty] private int _naniteMaxLevel;
    [ObservableProperty] private bool _useSatellites;
    [ObservableProperty] private int _shipyardMaxLevel;
    [ObservableProperty] private int _runs;
    [ObservableProperty] private int _iterationsPerRun;
    [ObservableProperty] private int _cpuPercent;

    public string CpuInfo => $"= {OptimizerOptions.ThreadsFor(CpuPercent)} of {Environment.ProcessorCount} threads";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsSchemaCommand))]
    private OptimizedOrder? _result;

    [ObservableProperty]
    private OptimizedOrder? _selectedSaved;

    [ObservableProperty]
    private string? _resultInfo;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _bonusesText;

    /// <summary>One line for the collapsed bonuses panel.</summary>
    [ObservableProperty]
    private string? _bonusesSummary;

    public IReadOnlyList<int> Slots { get; } = Enumerable.Range(1, PlanetConditions.SlotCount).ToList();

    public string ModelInfo =>
        $"Model {GameModel.Version}: costs, build times, production and energy measured on genesis; the account's bonuses are read from the game. " +
        "Fresh planet, one building at a time; satellites are built in the shipyard alongside.";

    private OptimizerSetup Setup => new()
    {
        MetalMine = Math.Clamp(MetalMine, 0, GameModel.MaxLevel),
        CrystalMine = Math.Clamp(CrystalMine, 0, GameModel.MaxLevel),
        DeuteriumSynthesizer = Math.Clamp(DeuteriumSynthesizer, 0, GameModel.MaxLevel),
        PlanetSlot = Math.Clamp(PlanetSlot, 1, PlanetConditions.SlotCount),
        MinTemperature = Math.Clamp(Math.Min(MinTemperature, MaxTemperature), -140, 260),
        MaxTemperature = Math.Clamp(Math.Max(MinTemperature, MaxTemperature), -140, 260),
        Bonuses = _bonuses,
        StartResources = new ResourceSet(Math.Max(0, StartMetal), Math.Max(0, StartCrystal), Math.Max(0, StartDeuterium)),
        SolarMaxLevel = Math.Clamp(SolarMaxLevel, 0, GameModel.MaxLevel),
        RoboticsMaxLevel = Math.Clamp(RoboticsMaxLevel, 0, GameModel.MaxLevel),
        NaniteMaxLevel = Math.Clamp(NaniteMaxLevel, 0, GameModel.MaxLevel),
        UseSatellites = UseSatellites,
        ShipyardMaxLevel = Math.Clamp(ShipyardMaxLevel, 0, GameModel.MaxLevel)
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(Result) or nameof(SelectedSaved) or nameof(ResultInfo) or nameof(StatusText) or nameof(ProgressPercent) or nameof(BonusesText) or nameof(BonusesSummary) or nameof(CpuInfo))
            return;

        if (e.PropertyName == nameof(CpuPercent))
            OnPropertyChanged(nameof(CpuInfo));

        if (e.PropertyName == nameof(PlanetSlot))
        {
            // A typical temperature for the new slot; the user can still change it.
            var typical = PlanetConditions.TypicalFor(PlanetSlot);
            _loading = true;
            MinTemperature = typical.MinTemperature;
            MaxTemperature = typical.MaxTemperature;
            _loading = false;
        }

        // A setup input changed: remember it and show the best order already found for it.
        SaveSettings();
        ShowResult(SavedResults.FirstOrDefault(r => r.SetupKey == Setup.Key), null);
    }

    partial void OnSelectedSavedChanged(OptimizedOrder? value)
    {
        if (value is null)
            return;

        ApplySetup(value.Setup with { Bonuses = _bonuses });
        if (Result is null)
        {
            // Found with other bonuses (they changed since): show it, a new search uses the current ones.
            ShowResult(value, null);
            ResultInfo += $" · simulated with other bonuses ({value.Setup.Bonuses.Signature})";
        }
    }

    /// <summary>Reads the bonuses from the game; keeps the last known ones when that isn't possible.</summary>
    [RelayCommand]
    private Task RefreshBonusesAsync() => RefreshBonusesAsync(CancellationToken.None);

    private async Task<bool> RefreshBonusesAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            StatusText = "No universe selected: using the last known bonuses.";
            return false;
        }

        try
        {
            StatusText = "Reading research, officers, academy and class from the game…";
            SetBonuses(await client.GetAccountBonusesAsync(cancellationToken));
            SaveSettings();
            ShowResult(SavedResults.FirstOrDefault(r => r.SetupKey == Setup.Key), null);
            StatusText = "Bonuses read from the game.";
            return true;
        }
        catch (Exception ex) when (ex is OgamexException or System.Net.Http.HttpRequestException)
        {
            StatusText = $"Reading the bonuses failed ({ex.Message}); using the last known ones.";
            return false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var refreshed = await RefreshBonusesAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;
        var bonusNote = refreshed ? string.Empty : " (bonuses not refreshed)";
        var setup = Setup;
        var runs = Math.Max(1, Runs);
        var options = new OptimizerOptions(runs, Math.Max(100, IterationsPerRun), Seed: Environment.TickCount, Threads: OptimizerOptions.ThreadsFor(CpuPercent));
        var previous = SavedResults.FirstOrDefault(r => r.SetupKey == setup.Key);
        StatusText = $"Running {runs} searches on {options.ThreadCount} of {Environment.ProcessorCount} threads{bonusNote}…";
        ProgressPercent = 0;

        var progress = new Progress<OptimizerProgress>(p =>
        {
            ProgressPercent = 100.0 * p.RunsDone / p.Runs;
            var speed = p.Elapsed.TotalSeconds > 0 ? p.Evaluations / p.Elapsed.TotalSeconds : 0;
            StatusText = $"Run {p.RunsDone}/{p.Runs} · best {OptimizedOrder.FormatDuration(p.BestSeconds)} · " +
                         $"{p.Evaluations:N0} orders simulated ({speed:N0}/s) · {p.Elapsed:hh\\:mm\\:ss}";
        });

        var found = await _optimizer.RunAsync(setup, options, progress, cancellationToken);
        if (found is null)
        {
            StatusText = cancellationToken.IsCancellationRequested ? "Stopped before any order was finished." : "No order reaches these targets.";
            return;
        }

        try
        {
            var best = _store.SaveIfBetter(found);
            ReplaceSaved(best);
            var improved = previous is null || found.TotalSeconds < previous.TotalSeconds;
            var comparison = previous is null
                ? "first result for this setup"
                : improved
                    ? $"better than the saved {OptimizedOrder.FormatDuration(previous.TotalSeconds)}"
                    : $"the saved {OptimizedOrder.FormatDuration(previous.TotalSeconds)} is still the best";
            StatusText = (cancellationToken.IsCancellationRequested ? "Stopped. " : "Done. ") +
                         $"This search: {OptimizedOrder.FormatDuration(found.TotalSeconds)} ({comparison}){bonusNote}.";
            ShowResult(best, previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowResult(found, previous);
            StatusText = $"Saving the result failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private void SaveAsSchema()
    {
        var order = Result!;
        var schema = order.ToSchema($"{order.Setup.ShortName} – {OptimizedOrder.FormatDuration(order.TotalSeconds)}");
        try
        {
            var library = _schemas.LoadLibrary().ToList();
            library.Add(schema);
            _schemas.SaveLibrary(library);
            _messenger.Send(new SchemaLibraryChangedMessage(schema.Id));
            StatusText = $"Saved as schema '{schema.Name}' ({schema.Steps.Count} steps) – see the Schemas page.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Saving the schema failed: {ex.Message}");
        }
    }

    private bool HasResult() => Result is not null;

    private void Load()
    {
        OptimizerData data;
        try
        {
            data = _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            data = new OptimizerData();
            StatusText = $"Reading the saved results failed: {ex.Message}";
        }

        foreach (var result in data.Results.Where(r => r.SetupKey == r.Setup.Key).OrderBy(r => r.Setup.ShortName))
            SavedResults.Add(result);
        _loading = true;
        Runs = data.Runs;
        IterationsPerRun = data.IterationsPerRun;
        CpuPercent = Math.Clamp(data.CpuPercent, 1, 100);
        _loading = false;
        SetBonuses(data.LastSetup.Bonuses);
        ApplySetup(data.LastSetup);
    }

    private void SetBonuses(AccountBonuses bonuses)
    {
        _bonuses = bonuses;
        var source = bonuses.ReadAt is { } readAt ? $"Read from the game {readAt:yyyy-MM-dd HH:mm}" : bonuses.Details;
        BonusesText = $"{bonuses}\n{(bonuses.ReadAt is null ? string.Empty : bonuses.Details + "\n")}{source}";
        var read = bonuses.ReadAt is { } at ? $"read {at:HH:mm}" : "not read yet";
        BonusesSummary = $"{bonuses.ClassName} · metal +{bonuses.Metal.Total:P0} · build +{bonuses.ConstructionSpeed:P0} · {read}";
    }

    private void ApplySetup(OptimizerSetup setup)
    {
        _loading = true;
        MetalMine = setup.MetalMine;
        CrystalMine = setup.CrystalMine;
        DeuteriumSynthesizer = setup.DeuteriumSynthesizer;
        PlanetSlot = setup.PlanetSlot;
        MinTemperature = setup.MinTemperature;
        MaxTemperature = setup.MaxTemperature;
        StartMetal = setup.StartResources.Metal;
        StartCrystal = setup.StartResources.Crystal;
        StartDeuterium = setup.StartResources.Deuterium;
        SolarMaxLevel = setup.SolarMaxLevel;
        RoboticsMaxLevel = setup.RoboticsMaxLevel;
        NaniteMaxLevel = setup.NaniteMaxLevel;
        UseSatellites = setup.UseSatellites;
        ShipyardMaxLevel = setup.ShipyardMaxLevel;
        _loading = false;
        SaveSettings();
        ShowResult(SavedResults.FirstOrDefault(r => r.SetupKey == Setup.Key), null);
    }

    private void SaveSettings()
    {
        try
        {
            _store.SaveSettings(Setup, Runs, IterationsPerRun, Math.Clamp(CpuPercent, 1, 100));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Saving the settings failed: {ex.Message}";
        }
    }

    private void ReplaceSaved(OptimizedOrder best)
    {
        var old = SavedResults.FirstOrDefault(r => r.SetupKey == best.SetupKey);
        if (old is not null)
            SavedResults[SavedResults.IndexOf(old)] = best;
        else
            SavedResults.Add(best);
    }

    private void ShowResult(OptimizedOrder? order, OptimizedOrder? previous)
    {
        Result = order;
        Steps.Clear();
        if (order is null)
        {
            ResultInfo = "No result for this setup yet – press Start.";
            return;
        }

        var number = 0;
        foreach (var step in order.Steps)
            Steps.Add(new OptimizedStepItem(step.IsSatellites ? 0 : ++number, step));

        var buildings = order.Steps.Where(s => !s.IsSatellites).ToList();
        var satellites = order.Steps.Where(s => s.IsSatellites).Sum(s => s.Level);
        var change = previous is not null && previous.TotalSeconds > order.TotalSeconds
            ? $" (was {OptimizedOrder.FormatDuration(previous.TotalSeconds)})"
            : string.Empty;
        ResultInfo = $"Best: {OptimizedOrder.FormatDuration(order.TotalSeconds)}{change} · {buildings.Count} buildings" +
                     (satellites > 0 ? $", {satellites} satellites" : string.Empty) +
                     $" · Robotics {Max(Gene.RoboticsFactory)}, Nanite {Max(Gene.NaniteFactory)}, Solar {Max(Gene.SolarPlant)}, Shipyard {Max(Gene.Shipyard)}" +
                     $" · found {order.FoundAt:yyyy-MM-dd HH:mm} · {order.Runs:N0} runs, {order.Evaluations:N0} orders simulated";

        int Max(Gene gene) => buildings.Where(s => s.Gene == gene).Select(s => s.Level).DefaultIfEmpty().Max();
    }
}

/// <summary>One row of the best order.</summary>
public sealed class OptimizedStepItem(int number, OptimizedStep step)
{
    public string Number => number > 0 ? $"{number}." : "+";

    public string Text => step.ToString() + (step.Forced ? " (storage full)" : string.Empty);

    public string Start => OptimizedOrder.FormatDuration(step.StartSeconds);

    public string Finish => OptimizedOrder.FormatDuration(step.FinishSeconds);

    public bool IsHelper => step.Gene > Gene.DeuteriumSynthesizer;
}
