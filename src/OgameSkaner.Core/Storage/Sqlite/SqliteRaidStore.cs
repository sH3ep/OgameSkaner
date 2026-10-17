using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>
/// Spy reports and fleets sent in the universe's SQLite database, all of them (the history); the settings stay in
/// <c>raid-settings.json</c>.
/// </summary>
public sealed class SqliteRaidStore(UniverseDatabases databases, IAppPaths paths) : IRaidStore
{
    private readonly JsonRaidStore _settings = new(paths);

    public RaidSettings LoadSettings(string universe) => _settings.LoadSettings(universe);

    public void SaveSettings(string universe, RaidSettings settings) => _settings.SaveSettings(universe, settings);

    public IReadOnlyList<EspionageReport> LoadReports(string universe)
    {
        using var connection = databases.Open(universe);
        return RaidTables.NewestReports(connection);
    }

    public IReadOnlySet<string> LoadReportIds(string universe, bool withDetailsOnly = false)
    {
        using var connection = databases.Open(universe);
        return RaidTables.ReportIds(connection, withDetailsOnly);
    }

    public void AddReports(string universe, IEnumerable<EspionageReport> reports)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        RaidTables.AddReports(connection, transaction, reports);
        transaction.Commit();
    }

    public IReadOnlyList<RaidRecord> LoadRaids(string universe)
    {
        using var connection = databases.Open(universe);
        return RaidTables.Raids(connection);
    }

    public void AddRaids(string universe, IEnumerable<RaidRecord> raids)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        RaidTables.AddRaids(connection, transaction, raids);
        transaction.Commit();
    }

    public RankingSnapshot LoadRanking(string universe, RankingCategory category)
    {
        using var connection = databases.Open(universe);
        return RaidTables.Ranking(connection, category);
    }

    public void SaveRanking(string universe, RankingCategory category, IReadOnlyList<RankingEntry> entries, DateTimeOffset readAt)
    {
        using var connection = databases.Open(universe);
        using var transaction = connection.BeginTransaction();
        RaidTables.ReplaceRanking(connection, transaction, category, entries, readAt);
        transaction.Commit();
    }
}
