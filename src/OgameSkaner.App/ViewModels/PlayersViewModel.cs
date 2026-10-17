using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

public sealed partial class PlayersViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly CsvExportService _csvExport;
    private readonly IDialogService _dialogs;
    private readonly IPlanetRepository _repository;
    private PlanetDatabase? _database;
    private int _historyRequest;

    public PlayersViewModel(IUniverseContext context, CsvExportService csvExport, IDialogService dialogs, IPlanetRepository repository)
    {
        _context = context;
        _csvExport = csvExport;
        _dialogs = dialogs;
        _repository = repository;

        _context.CurrentChanged += (_, _) =>
        {
            _database = null;
            Results = [];
            Summary = string.Empty;
        };
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _exactMatch;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    private IReadOnlyList<PlayerPlanetItemViewModel> _results = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private PlayerPlanetItemViewModel? _selectedResult;

    [ObservableProperty]
    private string _slotHistoryTitle = "Select a planet to see its history.";

    [ObservableProperty]
    private IReadOnlyList<HistoryEntry> _slotHistory = [];

    [ObservableProperty]
    private string _playerHistoryTitle = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<HistoryEntry> _playerHistory = [];

    public override async Task OnNavigatedToAsync()
    {
        if (_context.Current is null)
            return;

        _database ??= await _context.GetDatabaseAsync();
        RefreshResults();
    }

    partial void OnSearchTextChanged(string value) => RefreshResults();

    partial void OnExactMatchChanged(bool value) => RefreshResults();

    partial void OnSelectedResultChanged(PlayerPlanetItemViewModel? value) => _ = LoadHistoryAsync(value?.Planet);

    /// <summary>Loads the slot's and the player's history; a newer selection wins over a slower older one.</summary>
    private async Task LoadHistoryAsync(PlayerPlanet? planet)
    {
        var request = ++_historyRequest;
        if (planet is null || _context.Current is not { } universe)
        {
            (SlotHistoryTitle, SlotHistory, PlayerHistoryTitle, PlayerHistory) = ("Select a planet to see its history.", [], string.Empty, []);
            return;
        }

        try
        {
            var slot = await _repository.GetSlotHistoryAsync(universe.Name, planet.Coordinates);
            var player = await _repository.GetPlayerHistoryAsync(universe.Name, planet.PlayerId, planet.PlayerName);
            if (request != _historyRequest)
                return;

            SlotHistoryTitle = $"Planet {planet.Coordinates}";
            SlotHistory = HistoryText.Slot(slot);
            PlayerHistoryTitle = $"Player {planet.PlayerName}";
            PlayerHistory = HistoryText.Player(player);
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            if (request == _historyRequest)
                (SlotHistoryTitle, SlotHistory, PlayerHistoryTitle, PlayerHistory) = ($"History unavailable: {ex.Message}", [], string.Empty, []);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsvAsync()
    {
        var path = _dialogs.SaveFile("CSV files (*.csv)|*.csv", $"{_context.Current?.Name}-players.csv");
        if (path is null)
            return;

        try
        {
            await _csvExport.ExportAsync(Results.Select(r => r.Planet), path);
            _dialogs.ShowInfo($"Exported {Results.Count} planets to {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Export failed: {ex.Message}");
        }
    }

    private bool CanExportCsv() => Results.Count > 0;

    private void RefreshResults()
    {
        if (_database is null || _context.Client is not { } client)
            return;

        var planets = PlanetSearch.ByPlayerName(_database.Snapshot(), SearchText, ExactMatch);
        Results = planets.Select(p => new PlayerPlanetItemViewModel(p, client)).ToList();

        var players = planets.Select(p => p.PlayerName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Summary = $"{planets.Count:N0} planets, {players:N0} players";
    }
}
