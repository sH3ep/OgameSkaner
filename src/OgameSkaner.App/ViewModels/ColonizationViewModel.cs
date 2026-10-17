using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>Free planet slots, derived from the scanned systems of the galaxy database.</summary>
public sealed partial class ColonizationViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly ColonizationService _colonization;
    private PlanetDatabase? _database;

    public ColonizationViewModel(IUniverseContext context, ColonizationService colonization)
    {
        _context = context;
        _colonization = colonization;

        _context.CurrentChanged += (_, _) =>
        {
            _database = null;
            Results = [];
            Summary = string.Empty;
        };
    }

    [ObservableProperty]
    private int _galaxyFrom = 1;

    [ObservableProperty]
    private int _galaxyTo = 1;

    [ObservableProperty]
    private int _systemFrom = 1;

    [ObservableProperty]
    private int _systemTo = 1;

    [ObservableProperty]
    private int _positionFrom = 1;

    [ObservableProperty]
    private int _positionTo = ColonizationFilter.MaxPosition;

    [ObservableProperty]
    private IReadOnlyList<FreeSlotItemViewModel> _results = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    public override async Task OnNavigatedToAsync()
    {
        if (_context.Current is not { } universe)
            return;

        if (_database is null)
        {
            // A new universe starts with all of it; the database is set last so this refreshes once.
            GalaxyFrom = 1;
            GalaxyTo = universe.Galaxies;
            SystemFrom = 1;
            SystemTo = universe.Systems;
            PositionFrom = 1;
            PositionTo = ColonizationFilter.MaxPosition;
            _database = await _context.GetDatabaseAsync();
        }

        RefreshResults();
    }

    partial void OnGalaxyFromChanged(int value) => RefreshResults();

    partial void OnGalaxyToChanged(int value) => RefreshResults();

    partial void OnSystemFromChanged(int value) => RefreshResults();

    partial void OnSystemToChanged(int value) => RefreshResults();

    partial void OnPositionFromChanged(int value) => RefreshResults();

    partial void OnPositionToChanged(int value) => RefreshResults();

    private void RefreshResults()
    {
        if (_database is null || _context.Current is not { } universe || _context.Client is not { } client)
            return;

        var filter = new ColonizationFilter(new ScanRange(GalaxyFrom, SystemFrom, GalaxyTo, SystemTo), PositionFrom, PositionTo);
        var errors = filter.Validate(universe);
        if (errors.Count > 0)
        {
            Results = [];
            Summary = string.Join(" ", errors);
            return;
        }

        var result = ColonizationSearch.Find(_database.Snapshot(), _database.ScannedSystems(), filter);
        Results = result.Slots.Select(s => new FreeSlotItemViewModel(s, _colonization, client)).ToList();
        Summary = $"{result.Slots.Count:N0} free slots, {result.SystemsScanned:N0} of {result.SystemsInRange:N0} systems scanned";
    }
}
