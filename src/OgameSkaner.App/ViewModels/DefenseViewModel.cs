using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Services.Alerts;
using OgameSkaner.Core.Services.Defense;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// Attack protection: the incoming hostile fleets, the escape options (saved per universe on every change), the alert
/// channels (shared by all universes) and the continuous watch, which can start with the app.
/// </summary>
public sealed partial class DefenseViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly AttackMonitor _monitor;
    private readonly AlertDispatcher _alerts;
    private readonly IDefenseSettingsStore _store;
    private readonly IDialogService _dialogs;
    private string? _loadedUniverse;
    private bool _loading;

    public DefenseViewModel(IUniverseContext context, AttackMonitor monitor, AlertDispatcher alerts, IDefenseSettingsStore store, IDialogService dialogs,
        RunningModules running)
    {
        running.Track(this, RunCommand);
        _context = context;
        _monitor = monitor;
        _alerts = alerts;
        _store = store;
        _dialogs = dialogs;

        RunCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunCommand.IsRunning))
                NotifyProtection();
        };
        Threats.CollectionChanged += (_, _) => NotifyProtection();

        LoadAlertSettings();
        _context.CurrentChanged += async (_, _) =>
        {
            RunCancelCommand.Execute(null);
            Threats.Clear();
            StatusText = null;
            LoadSettings();
            await StartIfEnabledAsync();
        };
    }

    public ObservableCollection<ThreatItemViewModel> Threats { get; } = [];

    /// <summary>For the sidebar: off, watching, or hostile fleets on their way.</summary>
    public ProtectionStatus ProtectionStatus =>
        !RunCommand.IsRunning ? ProtectionStatus.Off : Threats.Count > 0 ? ProtectionStatus.Threatened : ProtectionStatus.Watching;

    public string ProtectionText
    {
        get
        {
            if (ProtectionStatus != ProtectionStatus.Threatened)
                return ProtectionStatus == ProtectionStatus.Off ? "Off" : "Watching";
            var attacks = Threats.Count(t => t.IsAttack);
            var next = Threats.Min(t => t.Threat.Movement.ArrivesAt).ToLocalTime();
            var what = attacks > 0 ? $"{attacks} attack(s)" : $"{Threats.Count} hostile fleet(s)";
            return $"{what}, next at {next:T}";
        }
    }

    public ObservableCollection<AlertChannelItemViewModel> Channels { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private int _checkIntervalSeconds;

    [ObservableProperty]
    private bool _notifyEspionage;

    [ObservableProperty]
    private FleetEscapeMode _escapeMode;

    [ObservableProperty]
    private bool _fallbackToNearestPlanet;

    [ObservableProperty]
    private int _escapeSpeedPercent;

    [ObservableProperty]
    private int _escapeSecondsBeforeArrival;

    [ObservableProperty]
    private bool _takeResources;

    /// <summary>Turn the escaped fleet back once the attack is over.</summary>
    [ObservableProperty]
    private bool _recallAfterAttack;

    [ObservableProperty]
    private int _recallDelaySeconds;

    [ObservableProperty]
    private bool _autoStart;

    /// <summary>Do everything except sending fleets (not saved).</summary>
    [ObservableProperty]
    private bool _testMode;

    [ObservableProperty]
    private string? _soundFile;

    [ObservableProperty]
    private int _soundRepeat;

    [ObservableProperty]
    private string? _statusText;

    public override Task OnNavigatedToAsync()
    {
        if (_loadedUniverse != _context.Current?.Name)
            LoadSettings();
        return Task.CompletedTask;
    }

    /// <summary>Starts the protection when <see cref="AutoStart"/> is on for the selected universe (app start, universe change).</summary>
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
        switch (e.PropertyName)
        {
            case nameof(StatusText) or nameof(TestMode) or nameof(ProtectionStatus) or nameof(ProtectionText):
                break;
            case nameof(SoundFile) or nameof(SoundRepeat):
                if (!_loading)
                    SaveAlertSettings();
                break;
            default:
                if (!_loading && _loadedUniverse is not null)
                    SaveSettings();
                break;
        }
    }

    /// <summary>Starts the running loop's next cycle now instead of at its "Next check" time.</summary>
    [RelayCommand]
    private void CheckNow() => _monitor.CheckNow();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var settings = CurrentSettings();
        StatusText = settings.TestMode ? "Watching in test mode (no fleet is sent)…" : "Watching for attacks…";
        AddLog($"Attack protection started ({Describe(settings)}). Option changes apply on the next start.");
        var result = await _monitor.RunAsync(client, settings, new Progress<DefenseProgress>(OnProgress), cancellationToken);

        StatusText = result.Outcome == DefenseOutcome.Cancelled
            ? $"Stopped. {result.Detected} hostile fleet(s) seen, {result.Escapes} escape(s)."
            : $"Protection stopped: {result.Error}";
        AddLog(StatusText);
        if (result.Outcome == DefenseOutcome.Failed)
            _dialogs.ShowError($"Attack protection stopped: {result.Error}");
    }

    [RelayCommand]
    private async Task TestAlertAsync()
    {
        if (!Channels.Any(c => c.IsEnabled))
        {
            AddLog("No alert channel is on.");
            return;
        }

        var failed = await _monitor.SendTestAlertAsync(_context.Current?.Name ?? "OgameSkaner");
        AddLog(failed.Count == 0 ? "Test alert sent." : $"Test alert failed on: {string.Join(", ", failed)} (see the log file).");
    }

    [RelayCommand]
    private void BrowseSound()
    {
        if (_dialogs.OpenFile("Sound (*.wav;*.mp3)|*.wav;*.mp3") is { } path)
            SoundFile = path;
    }

    [RelayCommand]
    private void ClearSound() => SoundFile = null;

    private void NotifyProtection()
    {
        OnPropertyChanged(nameof(ProtectionStatus));
        OnPropertyChanged(nameof(ProtectionText));
    }

    private void OnProgress(DefenseProgress progress)
    {
        if (progress.Threats is { } threats)
        {
            var now = DateTimeOffset.Now;
            Threats.Clear();
            foreach (var threat in threats)
                Threats.Add(new ThreatItemViewModel(threat, now));
            return;
        }

        AddLog(progress.Message);
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            _loadedUniverse = _context.Current?.Name;
            var settings = _loadedUniverse is null ? DefenseSettings.Default : _store.Load(_loadedUniverse);
            CheckIntervalSeconds = settings.CheckIntervalSeconds;
            NotifyEspionage = settings.NotifyEspionage;
            EscapeMode = settings.EscapeMode;
            FallbackToNearestPlanet = settings.FallbackToNearestPlanet;
            EscapeSpeedPercent = settings.EscapeSpeedPercent;
            EscapeSecondsBeforeArrival = settings.EscapeSecondsBeforeArrival;
            TakeResources = settings.TakeResources;
            RecallAfterAttack = settings.RecallAfterAttack;
            RecallDelaySeconds = settings.RecallDelaySeconds;
            AutoStart = settings.AutoStart;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AddLog($"Loading the defense settings failed: {ex.Message}");
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
            _store.Save(_loadedUniverse!, CurrentSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the defense settings failed: {ex.Message}");
        }
    }

    private void LoadAlertSettings()
    {
        _loading = true;
        try
        {
            AlertSettings settings;
            try
            {
                settings = _store.LoadAlerts();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                AddLog($"Loading the alert settings failed: {ex.Message}");
                settings = AlertSettings.Default;
            }

            Channels.Clear();
            foreach (var channel in _alerts.Channels)
                Channels.Add(new AlertChannelItemViewModel(channel, settings.IsEnabled(channel.Id), SaveAlertSettings));
            SoundFile = settings.SoundFile;
            SoundRepeat = settings.SoundRepeat;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveAlertSettings()
    {
        if (_loading)
            return;
        try
        {
            _store.SaveAlerts(new AlertSettings
            {
                EnabledChannels = Channels.Where(c => c.IsEnabled).Select(c => c.Id).ToList(),
                SoundFile = string.IsNullOrWhiteSpace(SoundFile) ? null : SoundFile,
                SoundRepeat = Math.Clamp(SoundRepeat, 1, 20)
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog($"Saving the alert settings failed: {ex.Message}");
        }
    }

    private DefenseSettings CurrentSettings() => new()
    {
        CheckIntervalSeconds = Math.Max(5, CheckIntervalSeconds),
        NotifyEspionage = NotifyEspionage,
        EscapeMode = EscapeMode,
        FallbackToNearestPlanet = FallbackToNearestPlanet,
        EscapeSpeedPercent = Math.Clamp(EscapeSpeedPercent, 1, 100),
        EscapeSecondsBeforeArrival = Math.Max(0, EscapeSecondsBeforeArrival),
        TakeResources = TakeResources,
        RecallAfterAttack = RecallAfterAttack,
        RecallDelaySeconds = Math.Max(0, RecallDelaySeconds),
        AutoStart = AutoStart,
        TestMode = TestMode
    };

    private static string Describe(DefenseSettings settings) => settings.EscapeMode switch
    {
        FleetEscapeMode.Off => $"alerts only, every {settings.CheckIntervalSeconds} s",
        FleetEscapeMode.Moon => $"escape to the moon{(settings.FallbackToNearestPlanet ? " or the nearest planet" : "")} at {settings.EscapeSpeedPercent}%, "
                                + $"{settings.EscapeSecondsBeforeArrival} s before impact, every {settings.CheckIntervalSeconds} s",
        _ => $"escape to the nearest planet at {settings.EscapeSpeedPercent}%, {settings.EscapeSecondsBeforeArrival} s before impact, every {settings.CheckIntervalSeconds} s"
    };

    /// <summary>The watch runs for days and logs every check, so old lines are dropped.</summary>
    private const int MaxLogLines = 1_000;

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:T}  {message}");
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(Log.Count - 1);
    }
}
