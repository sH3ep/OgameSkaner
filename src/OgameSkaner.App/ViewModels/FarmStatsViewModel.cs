using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services.Raiding;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.ViewModels;

/// <summary>One raid target's statistics as a grid row.</summary>
public sealed partial class FarmStatRowViewModel(RaidTarget target, DateTimeOffset now) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public RaidTarget Target { get; } = target;

    public string Top => Target.IsTop ? "★" : Target.IsHighValue ? "◆" : "";

    public string HighValue => Target.Stats?.HighValue is { } mark
        ? $"since {mark.Since.ToLocalTime():g}, loot {mark.Loot:N0}" + (mark.PoorSince > 0 ? $", {mark.PoorSince} poor since" : "")
        : "";

    public string PlayerName => Target.Planet.PlayerName;

    public int? Rank => Target.Planet.Rank;

    public Coordinates Coordinates => Target.Planet.Coordinates;

    public long CoordinatesSortKey => Coordinates.Galaxy * 1_000_000L + Coordinates.System * 100L + Coordinates.Position;

    public int Reports => Target.Stats?.Reports ?? 0;

    public string LastReport => Target.Stats is { } stats ? Age(now - stats.LastReport) : "";

    public double LastReportSortKey => Target.Stats is { } stats ? (now - stats.LastReport).TotalSeconds : double.MaxValue;

    public long? LastResources => Target.Stats?.LastResources;

    public long? MedianResources => Target.Stats?.MedianResources;

    public long? RefillPerHour => Target.Stats?.RefillPerHour;

    public long Score => Target.Stats?.Score ?? 0;

    public int Attacks => Target.Stats?.Attacks ?? 0;

    public long LootTaken => Target.Stats?.LootTaken ?? 0;

    public string Ban => Target.Ban is { } ban ? $"until {ban.Until.ToLocalTime():g} (ban {ban.Nth}): {ban.Reason}" : "";

    public bool IsBanned => Target.Ban is not null;

    public string State => Target.Note is { } note ? $"{Target.State}: {note}" : Target.State.ToString();

    private static string Age(TimeSpan age) =>
        age.TotalHours < 1 ? $"{Math.Max(0, age.TotalMinutes):0} min"
        : age.TotalDays < 1 ? $"{age.TotalHours:0} h"
        : $"{age.TotalDays:0.#} d";
}

/// <summary>
/// The raid targets' statistics over the stored spy reports (<see cref="TargetStatistics"/>): how rich each one is, the
/// top share the raider spies on first and the planets it marked low resource. Reads the database only.
/// </summary>
public sealed partial class FarmStatsViewModel : PageViewModel
{
    private readonly IUniverseContext _context;
    private readonly RaidRunner _runner;
    private readonly IRaidStore _store;
    private IReadOnlyList<RaidTarget> _targets = [];

    public FarmStatsViewModel(IUniverseContext context, RaidRunner runner, IRaidStore store)
    {
        _context = context;
        _runner = runner;
        _store = store;
        _context.CurrentChanged += (_, _) => _ = RefreshAsync();
    }

    [ObservableProperty]
    private IReadOnlyList<FarmStatRowViewModel> _rows = [];

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Show only the top targets.</summary>
    [ObservableProperty]
    private bool _topOnly;

    /// <summary>Show only the high-value planets.</summary>
    [ObservableProperty]
    private bool _highValueOnly;

    /// <summary>Show only the planets marked low resource.</summary>
    [ObservableProperty]
    private bool _bannedOnly;

    public override Task OnNavigatedToAsync() => RefreshAsync();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(TopOnly) or nameof(HighValueOnly) or nameof(BannedOnly))
            Show();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_context.Current is not { } universe)
        {
            _targets = [];
            Summary = "Select a universe first.";
            Show();
            return;
        }

        try
        {
            var settings = _store.LoadSettings(universe.Name);
            _targets = await Task.Run(() => _runner.GetTargetsAsync(universe.Name, settings));
            Show();
            var withStats = _targets.Where(t => t.Stats is not null).ToList();
            var top = withStats.Where(t => t.IsTop).ToList();
            Summary = $"The raid candidates' spy reports of the last {settings.StatsDays} days: {withStats.Count:N0} planets with reports, "
                      + (settings.TopTargetsPercent > 0
                          ? $"top {settings.TopTargetsPercent}% = {top.Count:N0} (score ≥ {(top.Count > 0 ? top.Min(t => t.Stats!.Score) : 0):N0}), spied on first; "
                          : "top targets off; ")
                      + $"{_targets.Count(t => t.IsHighValue):N0} high value (◆, a report with loot ≥ {settings.HighValueLootFactor} × min loot lately), "
                      + $"{_targets.Count(t => t.Ban is not null):N0} marked low resource. "
                      + "Score = a day of refill (gain per hour between reports, our attacks' expected loot added back), or the median stock seen while only one report is known.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidOperationException)
        {
            Summary = $"Reading the statistics failed: {ex.Message}";
        }
    }

    /// <summary>Lifts the selected planets' low-resource bans, so the raider spies on them again.</summary>
    [RelayCommand]
    private async Task LiftBansAsync()
    {
        if (_context.Current is not { } universe)
            return;

        var selected = Rows.Where(r => r.IsSelected && r.IsBanned).Select(r => r.Coordinates).ToList();
        if (selected.Count == 0)
        {
            Summary = "Tick the banned planets whose ban should be lifted.";
            return;
        }

        _runner.LiftBans(universe.Name, selected);
        await RefreshAsync();
    }

    private void Show() =>
        Rows = _targets
            .Where(t => t.Stats is not null || t.Ban is not null)
            .Where(t => !TopOnly || t.IsTop)
            .Where(t => !HighValueOnly || t.IsHighValue)
            .Where(t => !BannedOnly || t.Ban is not null)
            .OrderByDescending(t => t.Stats?.Score ?? -1)
            .Select(t => new FarmStatRowViewModel(t, DateTimeOffset.Now))
            .ToList();
}
