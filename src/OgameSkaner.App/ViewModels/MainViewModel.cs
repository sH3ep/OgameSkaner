using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IRecipient<SessionSavedMessage>
{
    private static readonly TimeSpan SessionCheckInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _services;
    private readonly IProfileStore _profiles;
    private readonly ISessionStore _sessions;
    private readonly IUniverseContext _context;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _sessionMonitor;

    public MainViewModel(
        IServiceProvider services,
        IEnumerable<PageDescriptor> pages,
        IProfileStore profiles,
        ISessionStore sessions,
        IUniverseContext context,
        IDialogService dialogs,
        IMessenger messenger,
        ILogger<MainViewModel> logger)
    {
        _services = services;
        _profiles = profiles;
        _sessions = sessions;
        _context = context;
        _dialogs = dialogs;
        _logger = logger;

        Pages = pages.ToList();
        messenger.Register(this);

        ReloadProfiles(profiles.SelectedName);
        SelectedPage = Pages.FirstOrDefault();
    }

    public IReadOnlyList<PageDescriptor> Pages { get; }

    public ObservableCollection<UniverseProfile> Profiles { get; } = [];

    [ObservableProperty]
    private UniverseProfile? _selectedProfile;

    [ObservableProperty]
    private PageDescriptor? _selectedPage;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private SessionStatus _sessionStatus;

    [ObservableProperty]
    private string _sessionStatusText = "No universe selected";

    partial void OnSelectedProfileChanged(UniverseProfile? value)
    {
        if (value is not null)
            _profiles.SelectedName = value.Name;

        _context.Select(value);
        RestartSessionMonitor();
        _ = CurrentPage?.OnNavigatedToAsync();
    }

    partial void OnSelectedPageChanged(PageDescriptor? value)
    {
        CurrentPage = value is null ? null : (PageViewModel)_services.GetRequiredService(value.ViewModelType);
        _ = CurrentPage?.OnNavigatedToAsync();
    }

    [RelayCommand]
    private void ManageProfiles()
    {
        _dialogs.ShowProfiles();
        ReloadProfiles(SelectedProfile?.Name);
    }

    [RelayCommand]
    private Task CheckSessionAsync() => CheckSessionCoreAsync(CancellationToken.None);

    public void Receive(SessionSavedMessage message)
    {
        if (message.Universe == SelectedProfile?.Name)
            RestartSessionMonitor();
    }

    private void ReloadProfiles(string? preferredName)
    {
        // Rebuilding the list resets the selection (and cancels a running scan), so only do it when something changed.
        var profiles = _profiles.GetAll();
        if (!profiles.SequenceEqual(Profiles))
        {
            Profiles.Clear();
            foreach (var profile in profiles)
                Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == preferredName) ?? Profiles.FirstOrDefault();
    }

    private void RestartSessionMonitor()
    {
        _sessionMonitor?.Cancel();
        _sessionMonitor?.Dispose();
        _sessionMonitor = new CancellationTokenSource();
        _ = MonitorSessionAsync(_sessionMonitor.Token);
    }

    private async Task MonitorSessionAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SessionCheckInterval);
        try
        {
            do
            {
                await CheckSessionCoreAsync(cancellationToken);
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckSessionCoreAsync(CancellationToken cancellationToken)
    {
        if (_context.Client is not { } client)
        {
            SetSessionStatus(SessionStatus.Unknown, "No universe selected");
            return;
        }

        if (_sessions.Get(client.Universe.Name) is not { IsComplete: true })
        {
            SetSessionStatus(SessionStatus.Invalid, "No session saved");
            return;
        }

        SetSessionStatus(SessionStatus.Checking, "Checking…");
        try
        {
            var valid = await client.CheckSessionAsync(cancellationToken);
            if (client == _context.Client)
                SetSessionStatus(valid ? SessionStatus.Valid : SessionStatus.Invalid, valid ? "Logged in" : "Session expired");
        }
        catch (OgamexException ex)
        {
            _logger.LogWarning(ex, "Session check failed");
            SetSessionStatus(SessionStatus.Unknown, ex.Message);
        }
    }

    private void SetSessionStatus(SessionStatus status, string text)
    {
        SessionStatus = status;
        SessionStatusText = text;
    }
}
