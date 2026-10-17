using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Storage;

/// <summary>The Raids page's settings, the espionage reports read and the fleets sent, per universe.</summary>
public interface IRaidStore
{
    RaidSettings LoadSettings(string universe);

    void SaveSettings(string universe, RaidSettings settings);

    /// <summary>The newest report per target (planet or moon at coordinates).</summary>
    IReadOnlyList<EspionageReport> LoadReports(string universe);

    /// <summary>The ids of the stored reports; with <paramref name="withDetailsOnly"/> only those whose detail page was read.</summary>
    IReadOnlySet<string> LoadReportIds(string universe, bool withDetailsOnly = false);

    /// <summary>Adds reports; a report already stored is updated (its ships and defenses are kept when the new one has none).</summary>
    void AddReports(string universe, IEnumerable<EspionageReport> reports);

    /// <summary>The fleets sent, oldest first.</summary>
    IReadOnlyList<RaidRecord> LoadRaids(string universe);

    void AddRaids(string universe, IEnumerable<RaidRecord> raids);

    /// <summary>The ranking as last read (<see cref="RankingSnapshot.Empty"/> before the first read).</summary>
    RankingSnapshot LoadRanking(string universe, RankingCategory category);

    /// <summary>Replaces the stored ranking with a complete new read.</summary>
    void SaveRanking(string universe, RankingCategory category, IReadOnlyList<RankingEntry> entries, DateTimeOffset readAt);
}

/// <summary>
/// Stores <c>universes/{name}/raid-settings.json</c>. The reports (<c>espionage-reports.json</c>, newest per target) and
/// fleets (<c>raids.json</c>, newest <see cref="MaxRaids"/>) are the former storage, now only read to migrate them into the
/// SQLite database (<see cref="Sqlite.SqliteRaidStore"/>).
/// </summary>
public sealed class JsonRaidStore(IAppPaths paths) : IRaidStore
{
    public const int MaxRaids = 1000;
    public const string ReportsFileName = "espionage-reports.json";
    public const string RaidsFileName = "raids.json";

    private readonly object _sync = new();

    public RaidSettings LoadSettings(string universe)
    {
        lock (_sync)
            return JsonFile.Read<RaidSettings>(FilePath(universe, "raid-settings.json")) ?? RaidSettings.Default;
    }

    public void SaveSettings(string universe, RaidSettings settings)
    {
        lock (_sync)
            JsonFile.Write(FilePath(universe, "raid-settings.json"), settings);
    }

    public IReadOnlyList<EspionageReport> LoadReports(string universe)
    {
        lock (_sync)
        {
            var document = JsonFile.Read<ReportsDocument>(FilePath(universe, ReportsFileName));
            return document?.Reports.Select(r => r.ToDomain()).ToList() ?? [];
        }
    }

    public IReadOnlySet<string> LoadReportIds(string universe, bool withDetailsOnly = false) =>
        LoadReports(universe).Where(r => !withDetailsOnly || r.Ships is not null || r.Defenses is not null).Select(r => r.Id).ToHashSet();

    public void AddReports(string universe, IEnumerable<EspionageReport> reports)
    {
        var added = reports.ToList();
        var newest = LoadReports(universe).Where(r => added.All(a => a.Id != r.Id)).Concat(added)
            .GroupBy(r => (r.Target, r.TargetType))
            .Select(g => g.MaxBy(r => r.Time)!)
            .OrderBy(r => r.Target.Galaxy).ThenBy(r => r.Target.System).ThenBy(r => r.Target.Position)
            .Select(ReportRecord.FromDomain)
            .ToList();

        lock (_sync)
            JsonFile.Write(FilePath(universe, ReportsFileName), new ReportsDocument { Reports = newest });
    }

    public IReadOnlyList<RaidRecord> LoadRaids(string universe)
    {
        lock (_sync)
        {
            var document = JsonFile.Read<RaidsDocument>(FilePath(universe, RaidsFileName));
            return document?.Raids.Select(r => r.ToDomain()).ToList() ?? [];
        }
    }

    /// <summary>Rankings were never kept in JSON.</summary>
    public RankingSnapshot LoadRanking(string universe, RankingCategory category) => RankingSnapshot.Empty;

    public void SaveRanking(string universe, RankingCategory category, IReadOnlyList<RankingEntry> entries, DateTimeOffset readAt) =>
        throw new NotSupportedException("Rankings are stored in the SQLite database.");

    public void AddRaids(string universe, IEnumerable<RaidRecord> raids)
    {
        var newest = LoadRaids(universe).Concat(raids).OrderByDescending(r => r.SentAt).Take(MaxRaids).Reverse().Select(RaidRow.FromDomain).ToList();
        lock (_sync)
            JsonFile.Write(FilePath(universe, RaidsFileName), new RaidsDocument { Raids = newest });
    }

    private string FilePath(string universe, string name) => Path.Combine(paths.UniverseDirectory(universe), name);

    private sealed class ReportsDocument
    {
        public List<ReportRecord> Reports { get; set; } = [];
    }

    private sealed class RaidsDocument
    {
        public List<RaidRow> Raids { get; set; } = [];
    }

    /// <summary>Flat on-disk shape, decoupled from the domain model.</summary>
    private sealed record ReportRecord(
        string Id,
        int Galaxy,
        int System,
        int Position,
        PlanetType Type,
        DateTimeOffset Time,
        string? Player,
        string? PlayerId,
        PlayerStatus Status,
        long Metal,
        long Crystal,
        long Deuterium,
        int? CounterEspionage,
        long? FleetPoints,
        Dictionary<string, long>? Ships,
        long? DefensePoints,
        Dictionary<string, long>? Defenses)
    {
        public EspionageReport ToDomain() => new(
            Id, new Coordinates(Galaxy, System, Position), Type, Time, Player, PlayerId, Status,
            new ResourceSet(Metal, Crystal, Deuterium), CounterEspionage, ToSection(FleetPoints, Ships), ToSection(DefensePoints, Defenses));

        public static ReportRecord FromDomain(EspionageReport r) => new(
            r.Id, r.Target.Galaxy, r.Target.System, r.Target.Position, r.TargetType, r.Time, r.PlayerName, r.PlayerId, r.PlayerStatus,
            r.Resources.Metal, r.Resources.Crystal, r.Resources.Deuterium, r.CounterEspionagePercent,
            r.Ships?.Points, FromUnits(r.Ships), r.Defenses?.Points, FromUnits(r.Defenses));

        private static UnitSection? ToSection(long? points, Dictionary<string, long>? units) =>
            points is { } p ? new UnitSection(p, (units ?? []).Select(u => new UnitCount(u.Key, u.Value)).ToList()) : null;

        private static Dictionary<string, long>? FromUnits(UnitSection? section) =>
            section?.Units.GroupBy(u => u.Unit).ToDictionary(g => g.Key, g => g.Sum(u => u.Quantity));
    }

    private sealed record RaidRow(
        int Galaxy,
        int System,
        int Position,
        PlanetType Type,
        string? Player,
        MissionType Mission,
        int OriginGalaxy,
        int OriginSystem,
        int OriginPosition,
        string ShipType,
        long Ships,
        long ExpectedLoot,
        DateTimeOffset SentAt,
        DateTimeOffset ArrivesAt,
        DateTimeOffset ReturnsAt)
    {
        public RaidRecord ToDomain() => new(
            new Coordinates(Galaxy, System, Position), Type, Player, Mission, new Coordinates(OriginGalaxy, OriginSystem, OriginPosition),
            ShipType, Ships, ExpectedLoot, SentAt, ArrivesAt, ReturnsAt);

        public static RaidRow FromDomain(RaidRecord r) => new(
            r.Target.Galaxy, r.Target.System, r.Target.Position, r.TargetType, r.PlayerName, r.Mission,
            r.Origin.Galaxy, r.Origin.System, r.Origin.Position, r.ShipType, r.Ships, r.ExpectedLoot, r.SentAt, r.ArrivesAt, r.ReturnsAt);
    }
}
