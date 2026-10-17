using System.Text.Json;
using Microsoft.Data.Sqlite;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>Reads and writes the <c>espionage_reports</c> and <c>raids</c> tables. Nothing is ever deleted.</summary>
internal static class RaidTables
{
    private const string ReportColumns =
        "id, galaxy, system, position, type, time, player, player_id, status, metal, crystal, deuterium, counter_espionage, fleet_points, ships, defense_points, defenses";

    private const string RaidColumns =
        "galaxy, system, position, type, player, mission, origin_galaxy, origin_system, origin_position, ship_type, ships, expected_loot, sent_at, arrives_at, returns_at";

    public static IReadOnlyList<EspionageReport> NewestReports(SqliteConnection connection)
    {
        using var command = Sql.Prepare(connection, null,
            $"""
            SELECT {ReportColumns} FROM (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY galaxy, system, position, type ORDER BY time DESC, id DESC) AS n
                FROM espionage_reports)
            WHERE n = 1 ORDER BY galaxy, system, position, type
            """);
        return ReadReports(command);
    }

    public static IReadOnlySet<string> ReportIds(SqliteConnection connection, bool withDetailsOnly)
    {
        using var command = Sql.Prepare(connection, null,
            "SELECT id FROM espionage_reports" + (withDetailsOnly ? " WHERE fleet_points IS NOT NULL OR defense_points IS NOT NULL" : ""));
        var ids = new HashSet<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>A report read again keeps the ships and defenses it had when the new read has none.</summary>
    public static void AddReports(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<EspionageReport> reports)
    {
        using var command = Sql.Prepare(connection, transaction,
            $"""
            INSERT INTO espionage_reports ({ReportColumns})
            VALUES ($id, $g, $s, $p, $type, $time, $player, $player_id, $status, $metal, $crystal, $deuterium, $counter,
                    $fleet_points, $ships, $defense_points, $defenses)
            ON CONFLICT (id) DO UPDATE SET
                galaxy = excluded.galaxy, system = excluded.system, position = excluded.position, type = excluded.type,
                time = excluded.time, player = excluded.player, player_id = excluded.player_id, status = excluded.status,
                metal = excluded.metal, crystal = excluded.crystal, deuterium = excluded.deuterium,
                counter_espionage = excluded.counter_espionage,
                fleet_points = COALESCE(excluded.fleet_points, fleet_points), ships = COALESCE(excluded.ships, ships),
                defense_points = COALESCE(excluded.defense_points, defense_points), defenses = COALESCE(excluded.defenses, defenses)
            """);

        foreach (var r in reports)
        {
            command.Set("$id", r.Id).Set("$g", r.Target.Galaxy).Set("$s", r.Target.System).Set("$p", r.Target.Position)
                .Set("$type", (int)r.TargetType).Set("$time", Sql.Ticks(r.Time)).Set("$player", r.PlayerName).Set("$player_id", r.PlayerId)
                .Set("$status", (int)r.PlayerStatus).Set("$metal", r.Resources.Metal).Set("$crystal", r.Resources.Crystal)
                .Set("$deuterium", r.Resources.Deuterium).Set("$counter", r.CounterEspionagePercent)
                .Set("$fleet_points", r.Ships?.Points).Set("$ships", Units(r.Ships))
                .Set("$defense_points", r.Defenses?.Points).Set("$defenses", Units(r.Defenses))
                .ExecuteNonQuery();
        }
    }

    public static IReadOnlyList<RaidRecord> Raids(SqliteConnection connection)
    {
        using var command = Sql.Prepare(connection, null, $"SELECT {RaidColumns} FROM raids ORDER BY sent_at, id");
        var raids = new List<RaidRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            raids.Add(new RaidRecord(
                new Coordinates(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                (PlanetType)reader.GetInt32(3),
                reader.NullableString(4),
                (MissionType)reader.GetInt32(5),
                new Coordinates(reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)),
                reader.GetString(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                Sql.Time(reader.GetInt64(12)),
                Sql.Time(reader.GetInt64(13)),
                Sql.Time(reader.GetInt64(14))));
        }

        return raids;
    }

    public static void AddRaids(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<RaidRecord> raids)
    {
        using var command = Sql.Prepare(connection, transaction,
            $"""
            INSERT INTO raids ({RaidColumns})
            VALUES ($g, $s, $p, $type, $player, $mission, $og, $os, $op, $ship_type, $ships, $loot, $sent, $arrives, $returns)
            """);

        foreach (var r in raids)
        {
            command.Set("$g", r.Target.Galaxy).Set("$s", r.Target.System).Set("$p", r.Target.Position).Set("$type", (int)r.TargetType)
                .Set("$player", r.PlayerName).Set("$mission", (int)r.Mission)
                .Set("$og", r.Origin.Galaxy).Set("$os", r.Origin.System).Set("$op", r.Origin.Position)
                .Set("$ship_type", r.ShipType).Set("$ships", r.Ships).Set("$loot", r.ExpectedLoot)
                .Set("$sent", Sql.Ticks(r.SentAt)).Set("$arrives", Sql.Ticks(r.ArrivesAt)).Set("$returns", Sql.Ticks(r.ReturnsAt))
                .ExecuteNonQuery();
        }
    }

    /// <summary>The ranking last read; empty when it was never read.</summary>
    public static RankingSnapshot Ranking(SqliteConnection connection, RankingCategory category)
    {
        var readAt = Sql.Scalar(connection, $"SELECT value FROM meta WHERE key = '{RankingReadKey(category)}'") is string at
            ? new DateTimeOffset(long.Parse(at, System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Zero).ToLocalTime()
            : (DateTimeOffset?)null;
        using var command = Sql.Prepare(connection, null, "SELECT player_id, points FROM rankings WHERE category = $category");
        command.Set("$category", category.ToString());
        var points = new Dictionary<string, long>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            points[reader.GetString(0)] = reader.GetInt64(1);
        return readAt is null && points.Count == 0 ? RankingSnapshot.Empty : new RankingSnapshot(readAt, points);
    }

    /// <summary>Replaces the category's ranking with a complete new read.</summary>
    public static void ReplaceRanking(SqliteConnection connection, SqliteTransaction transaction, RankingCategory category, IEnumerable<RankingEntry> entries, DateTimeOffset readAt)
    {
        using (var delete = Sql.Prepare(connection, transaction, "DELETE FROM rankings WHERE category = $category"))
            delete.Set("$category", category.ToString()).ExecuteNonQuery();

        using var insert = Sql.Prepare(connection, transaction,
            """
            INSERT INTO rankings (category, player_id, points, rank) VALUES ($category, $player, $points, $rank)
            ON CONFLICT (category, player_id) DO UPDATE SET points = excluded.points, rank = excluded.rank
            """);
        foreach (var entry in entries)
            insert.Set("$category", category.ToString()).Set("$player", entry.PlayerId).Set("$points", entry.Points).Set("$rank", entry.Rank).ExecuteNonQuery();

        using var read = Sql.Prepare(connection, transaction,
            "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value");
        read.Set("$key", RankingReadKey(category)).Set("$value", Sql.Ticks(readAt).ToString(System.Globalization.CultureInfo.InvariantCulture)).ExecuteNonQuery();
    }

    private static string RankingReadKey(RankingCategory category) => $"ranking_{category}_read_at";

    private static IReadOnlyList<EspionageReport> ReadReports(SqliteCommand command)
    {
        var reports = new List<EspionageReport>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            reports.Add(new EspionageReport(
                reader.GetString(0),
                new Coordinates(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
                (PlanetType)reader.GetInt32(4),
                Sql.Time(reader.GetInt64(5)),
                reader.NullableString(6),
                reader.NullableString(7),
                (PlayerStatus)reader.GetInt32(8),
                new ResourceSet(reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11)),
                reader.NullableInt32(12),
                Section(reader.NullableInt64(13), reader.NullableString(14)),
                Section(reader.NullableInt64(15), reader.NullableString(16))));
        }

        return reports;
    }

    /// <summary>The units of a section as JSON (<c>[{"unit":"LIGHT_FIGHTER","quantity":5}]</c>), in the report's order.</summary>
    private static string? Units(UnitSection? section) =>
        section is null ? null : JsonSerializer.Serialize(section.Units, JsonFile.CompactOptions);

    private static UnitSection? Section(long? points, string? units) =>
        points is { } p
            ? new UnitSection(p, units is null ? [] : JsonSerializer.Deserialize<List<UnitCount>>(units, JsonFile.CompactOptions) ?? [])
            : null;
}
