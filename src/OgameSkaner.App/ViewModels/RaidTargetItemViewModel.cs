using CommunityToolkit.Mvvm.ComponentModel;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Services.Raiding;

namespace OgameSkaner.App.ViewModels;

/// <summary>One inactive planet in the Raids page's table.</summary>
public sealed partial class RaidTargetItemViewModel(RaidTarget target, DateTimeOffset now) : ObservableObject
{
    public RaidTarget Target { get; } = target;

    [ObservableProperty]
    private bool _isSelected;

    public string PlayerName => Target.Planet.PlayerName;

    public int? Rank => Target.Planet.Rank;

    public string Status => Target.Planet.Status.ToLetters();

    public string Coordinates => Target.Planet.Coordinates.ToString();

    /// <summary>Sorts the coordinates by galaxy, system and position (the text would sort "10:…" before "2:…").</summary>
    public long CoordinatesSortKey => Target.Planet.Coordinates.Galaxy * 1_000_000L + Target.Planet.Coordinates.System * 100L + Target.Planet.Coordinates.Position;

    public string ReportAge => Target.Report is { } report ? Age(now - report.Time) : "";

    /// <summary>Sorts the report age by time ("10 min" would sort before "2 h"); no report sorts last.</summary>
    public double ReportAgeSortKey => Target.Report is { } report ? (now - report.Time).TotalSeconds : double.MaxValue;

    public long? Resources => Target.Report?.TotalResources;

    public long? DefensePoints => Target.Report?.DefensePoints;

    public long? FleetPoints => Target.Report?.FleetPoints;

    /// <summary>The player's defense points in the ranking; 0 = no defenses, safe to attack. Null until the ranking is read.</summary>
    public long? RankingDefensePoints => Target.RankingDefensePoints;

    public long? Loot => Target.Report is null ? null : Target.ExpectedLoot;

    public RaidTargetState State => Target.State;

    public string StateText => Target.Note is { } note ? $"{Target.State}: {note}" : Target.State.ToString();

    /// <summary>Units seen by the probes, for the tooltip.</summary>
    public string? Units => Target.Report is { } report
        ? string.Join(Environment.NewLine, new[] { report.Ships, report.Defenses }
            .Where(s => s is not null)
            .SelectMany(s => s!.Units)
            .Select(u => $"{UnitCatalog.NameOf(u.Unit)}: {u.Quantity:N0}")
            .DefaultIfEmpty("No units seen"))
        : null;

    private static string Age(TimeSpan age) =>
        age.TotalMinutes < 1 ? "now"
        : age.TotalHours < 1 ? $"{age.TotalMinutes:0} min"
        : age.TotalDays < 1 ? $"{age.TotalHours:0} h"
        : $"{age.TotalDays:0} d";
}
