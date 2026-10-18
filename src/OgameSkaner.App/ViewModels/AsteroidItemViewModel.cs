using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Services.Asteroids;

namespace OgameSkaner.App.ViewModels;

/// <summary>One asteroid in the Asteroids page's table.</summary>
public sealed class AsteroidItemViewModel(AsteroidTarget target, DateTimeOffset now)
{
    public AsteroidTarget Target { get; } = target;

    public AsteroidState State => Target.State;

    public string Coordinates => Target.Asteroid.Coordinates.ToString();

    /// <summary>Sorts the coordinates by galaxy and system (the text would sort "10:…" before "2:…").</summary>
    public long CoordinatesSortKey => Target.Asteroid.Coordinates.Galaxy * 1_000L + Target.Asteroid.Coordinates.System;

    public string Seen => Target.Asteroid.SeenAt.ToLocalTime().ToString("T");

    public string GoneAt => Target.Asteroid.DisappearsAt.ToLocalTime().ToString("T");

    public string TimeLeft => Target.Asteroid.DisappearsAt > now ? Format(Target.Asteroid.DisappearsAt - now) : "";

    public DateTimeOffset GoneAtSortKey => Target.Asteroid.DisappearsAt;

    public long? Miners => Target.Mission?.Miners;

    public long? Estimate => Target.Mission is { EstimatedResources: > 0 } mission ? mission.EstimatedResources : null;

    public string Arrival => Target.Mission?.ArrivesAt.ToLocalTime().ToString("T") ?? "";

    public string StateText => State switch
    {
        AsteroidState.Open => "Open",
        AsteroidState.Sent => "Miners on the way",
        AsteroidState.Mined => "Mined",
        _ => "Gone"
    };

    private static string Format(TimeSpan span) => span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"mm\:ss");
}

/// <summary>One mining report in the Asteroids page's table.</summary>
public sealed class AsteroidReportItemViewModel(AsteroidReport report)
{
    public AsteroidReport Report { get; } = report;

    public string Time => Report.Time.ToLocalTime().ToString("g");

    public DateTimeOffset TimeSortKey => Report.Time;

    public string Coordinates => Report.Target.ToString();

    public AsteroidResultKind Kind => Report.Kind;

    public long? Available => Report.Available > 0 ? Report.Available : null;

    public long Gained => Report.Gained.Metal + Report.Gained.Crystal + Report.Gained.Deuterium;

    public long? DarkMatter => Report.DarkMatter > 0 ? Report.DarkMatter : null;

    public string Resources => Report.Kind == AsteroidResultKind.Resources
        ? $"M {Report.Gained.Metal:N0} / C {Report.Gained.Crystal:N0} / D {Report.Gained.Deuterium:N0}"
        : "";
}
