using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

public sealed partial class ScannerViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly GalaxyScanService _scanService;
    private readonly ISessionStore _sessions;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly IScanSettingsStore _settingsStore;
    private bool _loading;

    public ScannerViewModel(
        IUniverseContext context,
        GalaxyScanService scanService,
        ISessionStore sessions,
        IDialogService dialogs,
        IMessenger messenger,
        IScanSettingsStore settingsStore,
        RunningModules running)
    {
        running.Track(this, ScanCommand);
        _context = context;
        _scanService = scanService;
        _sessions = sessions;
        _dialogs = dialogs;
        _messenger = messenger;
        _settingsStore = settingsStore;
        LoadSettings();

        // A running scan belongs to the universe it was started for.
        _context.CurrentChanged += (_, _) =>
        {
            ScanCancelCommand.Execute(null);
            StatusText = null;
            ProgressValue = 0;
            LoadSettings();
        };
    }

    // Pacing (ScanSettings), saved per universe as soon as a value changes.
    [ObservableProperty]
    private int _minDelayMilliseconds;

    [ObservableProperty]
    private int _maxDelayMilliseconds;

    [ObservableProperty]
    private int _pauseEverySystems;

    [ObservableProperty]
    private int _minPauseSeconds;

    [ObservableProperty]
    private int _maxPauseSeconds;

    partial void OnMinDelayMillisecondsChanged(int value) => SaveSettings();

    partial void OnMaxDelayMillisecondsChanged(int value) => SaveSettings();

    partial void OnPauseEverySystemsChanged(int value) => SaveSettings();

    partial void OnMinPauseSecondsChanged(int value) => SaveSettings();

    partial void OnMaxPauseSecondsChanged(int value) => SaveSettings();

    private ScanSettings Settings => new()
    {
        MinDelayMilliseconds = Math.Max(0, MinDelayMilliseconds),
        MaxDelayMilliseconds = Math.Max(0, MaxDelayMilliseconds),
        PauseEverySystems = Math.Max(0, PauseEverySystems),
        MinPauseSeconds = Math.Max(0, MinPauseSeconds),
        MaxPauseSeconds = Math.Max(0, MaxPauseSeconds)
    };

    private void LoadSettings()
    {
        var settings = _context.Current is { } universe ? _settingsStore.Load(universe.Name) : ScanSettings.Default;
        _loading = true;
        try
        {
            MinDelayMilliseconds = settings.MinDelayMilliseconds;
            MaxDelayMilliseconds = settings.MaxDelayMilliseconds;
            PauseEverySystems = settings.PauseEverySystems;
            MinPauseSeconds = settings.MinPauseSeconds;
            MaxPauseSeconds = settings.MaxPauseSeconds;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSettings()
    {
        if (!_loading && _context.Current is { } universe)
            _settingsStore.Save(universe.Name, Settings);
    }

    [ObservableProperty]
    private int _startGalaxy = 1;

    [ObservableProperty]
    private int _startSystem = 1;

    [ObservableProperty]
    private int _endGalaxy = 1;

    [ObservableProperty]
    private int _endSystem = 1;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 1;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSessionCommand))]
    private string _authToken = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSessionCommand))]
    private string _sessionId = string.Empty;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        var range = new ScanRange(StartGalaxy, StartSystem, EndGalaxy, EndSystem);
        var errors = range.Validate(client.Universe);
        if (errors.Count > 0)
        {
            _dialogs.ShowError(string.Join(Environment.NewLine, errors));
            return;
        }

        ProgressValue = 0;
        ProgressMaximum = range.Count;
        StatusText = "Starting…";

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressValue = p.Completed;
            StatusText = $"Scanned {p.Current} ({p.Completed}/{p.Total}) – {p.PlanetsInSystem} planets"
                         + (p.PauseUntil is { } until ? $", pausing until {until.ToLocalTime():T}" : "");
        });

        var result = await _scanService.ScanAsync(client, range, progress, Settings, cancellationToken);

        StatusText = result.Outcome switch
        {
            ScanOutcome.Completed => $"Finished: {result.SystemsScanned} systems scanned.",
            ScanOutcome.Cancelled => $"Cancelled after {result.SystemsScanned} of {result.Total} systems.",
            _ => $"Stopped after {result.SystemsScanned} of {result.Total} systems."
        };

        if (result.Outcome == ScanOutcome.Failed)
            _dialogs.ShowError(result.Error ?? "Scan failed.");
    }

    [RelayCommand(CanExecute = nameof(CanSaveSession))]
    private void SaveSession()
    {
        if (_context.Current is not { } universe)
        {
            _dialogs.ShowError("Select a universe first.");
            return;
        }

        _sessions.Save(universe.Name, new OgamexSession(AuthToken.Trim(), SessionId.Trim()));
        AuthToken = string.Empty;
        SessionId = string.Empty;
        StatusText = $"Session saved for {universe.Name}.";
        _messenger.Send(new SessionSavedMessage(universe.Name));
    }

    private bool CanSaveSession() => !string.IsNullOrWhiteSpace(AuthToken) && !string.IsNullOrWhiteSpace(SessionId);
}
