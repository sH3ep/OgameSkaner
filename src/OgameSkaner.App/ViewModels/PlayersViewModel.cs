using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

public sealed partial class PlayersViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly CsvExportService _csvExport;
    private readonly IDialogService _dialogs;
    private PlanetDatabase? _database;

    public PlayersViewModel(IUniverseContext context, CsvExportService csvExport, IDialogService dialogs)
    {
        _context = context;
        _csvExport = csvExport;
        _dialogs = dialogs;

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

    public override async Task OnNavigatedToAsync()
    {
        if (_context.Current is null)
            return;

        _database ??= await _context.GetDatabaseAsync();
        RefreshResults();
    }

    partial void OnSearchTextChanged(string value) => RefreshResults();

    partial void OnExactMatchChanged(bool value) => RefreshResults();

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
