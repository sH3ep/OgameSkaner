using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services.Asteroids;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// Asteroid mining: the asteroids found with the miners sent to them, the mining reports and the estimate they give,
/// the settings (saved per universe on every change), a continuous run and the manual find/send actions.
/// </summary>
public sealed partial class AsteroidsViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly AsteroidRunner _runner;
    private readonly IAsteroidStore _store;
    private readonly IDialogService _dialogs;
    private string? _loadedUniverse;
    private bool _loading;

    public AsteroidsViewModel(IUniverseContext context, AsteroidRunner runner, IAsteroidStore store, IDialogService dialogs, RunningModules running)
    {
        running.Track(this, RunCommand);
        _context = context;
        _runner = runner;
        _store = store;
        _dialogs = dialogs;

        _context.CurrentChanged += (_, _) =>
        {
            RunCancelCommand.Execute(null);
            StatusText = null;
            LoadSettings();
            Refresh();
        };

        RunCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunCommand.IsRunning))
                NotifyManualCommands();
        };
    }

    public ObservableCollection<AsteroidItemViewModel> Asteroids { get; } = [];

    public ObservableCollection<AsteroidReportItemViewModel> Reports { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendSelectedCommand))]
    private AsteroidItemViewModel? _selectedAsteroid;

    [ObservableProperty]
    private int _marginPercent;

    [ObservableProperty]
    private long _minersWithoutEstimate;

    [ObservableProperty]
    private long _minMiners;

    [ObservableProperty]
    private int _arrivalSafetySeconds;

    [ObservableProperty]
    private int _fleetSlotUsePercent;

    [ObservableProperty]
    private int _checkIntervalMinutes;

    [ObservableProperty]
    private int _speedPercentage;

    /// <summary>Do everything except sending fleets (not saved).</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string? _estimateInfo;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(ReadReportsCommand), nameof(SendSelectedCommand))]
    private bool _isBusy;

    public override Task OnNavigatedToAsync()
    {
        if (_loadedUniverse != _context.Current?.Name)
            LoadSettings();
        Refresh();
        return Task.CompletedTask;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(SelectedAsteroid) or nameof(EstimateInfo) or nameof(StatusText) or nameof(IsBusy) or nameof(TestMode):
                break;
            default:
                if (!_loading && _loadedUniverse is not null)
                {
                    SaveSettings();
                    Refresh();
                }
                break;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        Asteroids.Clear();
        Reports.Clear();
        if (_context.Current is not { } universe)
        {
            EstimateInfo = null;
            return;
        }

        AsteroidOverview overview;
        try
        {
            overview = _runner.GetOverview(universe.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            AddLog($"Loading asteroid data failed: {ex.Message}");
            return;
        }

        var now = DateTimeOffset.Now;
        var settings = CurrentSettings();
        foreach (var target in overview.Asteroids)
            Asteroids.Add(new AsteroidItemViewModel(target, now));
        foreach (var report in overview.Reports)
            Reports.Add(new AsteroidReportItemViewModel(report));

        var gains = $"{overview.SuccessesToday} successful mining(s) today (UTC), gains at {AsteroidEstimator.Multiplier(overview.SuccessesToday):P0}";
        EstimateInfo = overview.Estimate is { } estimate
            ? $"Next asteroid: ~{estimate.Resources:N0} resources (from the report of {estimate.Basis.Time.ToLocalTime():g}), "
              + $"so miners for {estimate.Resources * (1 + settings.MarginPercent / 100.0):N0} cargo; {gains}."
            : $"No mining report with resources yet: {settings.MinersWithoutEstimate:N0} miners per asteroid. {gains}.";
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task FindAsync() =>
        ActAsync(client => _runner.FindAsync(client, Progress()), n => $"{n} new asteroid(s) found");

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ReadReportsAsync() =>
        ActAsync(client => _runner.ReadReportsAsync(client, Progress()), n => $"{n} new mining report(s)");

    [RelayCommand(CanExecute = nameof(CanSendSelected))]
    private Task SendSelectedAsync()
    {
        if (SelectedAsteroid is not { State: AsteroidState.Open } selected)
            return Task.CompletedTask;
        var target = selected.Target.Asteroid.Coordinates;
        return ActAsync(client => _runner.SendAsync(client, CurrentSettings(), Progress(), only: [target]), n => $"{n} mining fleet(s) sent");
    }

    private bool CanAct() => !IsBusy && !RunCommand.IsRunning;

    private bool CanSendSelected() => CanAct() && SelectedAsteroid is { State: AsteroidState.Open };

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        StatusText = TestMode ? "Running in test mode (nothing is sent)…" : "Running…";
        AddLog(TestMode ? "Asteroid miner started in test mode" : "Asteroid miner started");
        var result = await _runner.RunAsync(client, CurrentSettings(), Progress(refreshOnCycle: true), cancellationToken);

        StatusText = result.Outcome == AsteroidOutcome.Cancelled
            ? $"Stopped after finding {result.Found} asteroid(s) and sending {result.Sent} mining fleet(s)."
            : $"Failed after finding {result.Found} asteroid(s) and sending {result.Sent} mining fleet(s).";
        AddLog(StatusText);
        Refresh();

        if (result.Outcome == AsteroidOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "The asteroid miner failed.");
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

        Refresh();
    }

    private IProgress<AsteroidProgress> Progress(bool refreshOnCycle = false) => new Progress<AsteroidProgress>(p =>
    {
        AddLog(p.Message);
        if (refreshOnCycle && p.Message.StartsWith("Next check", StringComparison.Ordinal))
            Refresh();
    });

    private void NotifyManualCommands()
    {
        FindCommand.NotifyCanExecuteChanged();
        ReadReportsCommand.NotifyCanExecuteChanged();
        SendSelectedCommand.NotifyCanExecuteChanged();
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            var settings = _loadedUniverse is null ? AsteroidSettings.Default : _store.LoadSettings(_loadedUniverse);
            MarginPercent = settings.MarginPercent;
            MinersWithoutEstimate = settings.MinersWithoutEstimate;
            MinMiners = settings.MinMiners;
            ArrivalSafetySeconds = settings.ArrivalSafetySeconds;
            FleetSlotUsePercent = settings.FleetSlotUsePercent;
            CheckIntervalMinutes = settings.CheckIntervalMinutes;
            SpeedPercentage = settings.SpeedPercentage;
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
            AddLog($"Saving asteroid settings failed: {ex.Message}");
        }
    }

    private AsteroidSettings CurrentSettings() => new()
    {
        MarginPercent = Math.Max(0, MarginPercent),
        MinersWithoutEstimate = Math.Max(1, MinersWithoutEstimate),
        MinMiners = Math.Max(1, MinMiners),
        ArrivalSafetySeconds = Math.Max(0, ArrivalSafetySeconds),
        FleetSlotUsePercent = Math.Clamp(FleetSlotUsePercent, 0, 100),
        CheckIntervalMinutes = Math.Max(1, CheckIntervalMinutes),
        SpeedPercentage = Math.Clamp(SpeedPercentage, 1, 100),
        TestMode = TestMode
    };

    private void AddLog(string message) => Log.Insert(0, $"{DateTime.Now:T}  {message}");
}
