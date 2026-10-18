using Microsoft.Data.Sqlite;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>Reads and writes the <c>asteroids</c>, <c>asteroid_missions</c> and <c>asteroid_reports</c> tables. Nothing is ever deleted.</summary>
internal static class AsteroidTables
{
    private const string MissionColumns =
        "galaxy, system, position, origin_galaxy, origin_system, origin_position, miners, estimated_resources, sent_at, arrives_at, returns_at";

    private const string ReportColumns = "id, time, galaxy, system, position, kind, available, metal, crystal, deuterium, dark_matter";

    /// <summary>The asteroids that disappear after <paramref name="after"/>, soonest first.</summary>
    public static IReadOnlyList<Asteroid> Asteroids(SqliteConnection connection, DateTimeOffset after)
    {
        using var command = Sql.Prepare(connection, null,
            "SELECT galaxy, system, position, disappears_at, seen_at FROM asteroids WHERE disappears_at > $after ORDER BY disappears_at, id");
        command.Set("$after", Sql.Ticks(after));
        var asteroids = new List<Asteroid>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            asteroids.Add(new Asteroid(
                new Coordinates(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)), Sql.Time(reader.GetInt64(3)), Sql.Time(reader.GetInt64(4))));
        }

        return asteroids;
    }

    /// <summary>
    /// Adds an asteroid, or updates the one still there at the same coordinates (each scan works out the disappearance
    /// from the seconds left, which can differ by a second).
    /// </summary>
    public static void SaveAsteroid(SqliteConnection connection, SqliteTransaction transaction, Asteroid asteroid)
    {
        using var update = Sql.Prepare(connection, transaction,
            """
            UPDATE asteroids SET disappears_at = $disappears
            WHERE galaxy = $g AND system = $s AND position = $p AND disappears_at > $seen
            """);
        var updated = update.Set("$g", asteroid.Coordinates.Galaxy).Set("$s", asteroid.Coordinates.System).Set("$p", asteroid.Coordinates.Position)
            .Set("$disappears", Sql.Ticks(asteroid.DisappearsAt)).Set("$seen", Sql.Ticks(asteroid.SeenAt))
            .ExecuteNonQuery();
        if (updated > 0)
            return;

        using var insert = Sql.Prepare(connection, transaction,
            "INSERT INTO asteroids (galaxy, system, position, seen_at, disappears_at) VALUES ($g, $s, $p, $seen, $disappears)");
        insert.Set("$g", asteroid.Coordinates.Galaxy).Set("$s", asteroid.Coordinates.System).Set("$p", asteroid.Coordinates.Position)
            .Set("$seen", Sql.Ticks(asteroid.SeenAt)).Set("$disappears", Sql.Ticks(asteroid.DisappearsAt))
            .ExecuteNonQuery();
    }

    public static IReadOnlyList<AsteroidMission> Missions(SqliteConnection connection)
    {
        using var command = Sql.Prepare(connection, null, $"SELECT {MissionColumns} FROM asteroid_missions ORDER BY sent_at, id");
        var missions = new List<AsteroidMission>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            missions.Add(new AsteroidMission(
                new Coordinates(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                new Coordinates(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)),
                reader.GetInt64(6),
                reader.GetInt64(7),
                Sql.Time(reader.GetInt64(8)),
                Sql.Time(reader.GetInt64(9)),
                Sql.Time(reader.GetInt64(10))));
        }

        return missions;
    }

    public static void AddMissions(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<AsteroidMission> missions)
    {
        using var command = Sql.Prepare(connection, transaction,
            $"""
            INSERT INTO asteroid_missions ({MissionColumns})
            VALUES ($g, $s, $p, $og, $os, $op, $miners, $estimate, $sent, $arrives, $returns)
            """);

        foreach (var m in missions)
        {
            command.Set("$g", m.Target.Galaxy).Set("$s", m.Target.System).Set("$p", m.Target.Position)
                .Set("$og", m.Origin.Galaxy).Set("$os", m.Origin.System).Set("$op", m.Origin.Position)
                .Set("$miners", m.Miners).Set("$estimate", m.EstimatedResources)
                .Set("$sent", Sql.Ticks(m.SentAt)).Set("$arrives", Sql.Ticks(m.ArrivesAt)).Set("$returns", Sql.Ticks(m.ReturnsAt))
                .ExecuteNonQuery();
        }
    }

    public static IReadOnlyList<AsteroidReport> Reports(SqliteConnection connection)
    {
        using var command = Sql.Prepare(connection, null, $"SELECT {ReportColumns} FROM asteroid_reports ORDER BY time, id");
        var reports = new List<AsteroidReport>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            reports.Add(new AsteroidReport(
                reader.GetString(0),
                Sql.Time(reader.GetInt64(1)),
                new Coordinates(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)),
                (AsteroidResultKind)reader.GetInt32(5),
                reader.GetInt64(6),
                new ResourceSet(reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9)),
                reader.GetInt64(10)));
        }

        return reports;
    }

    public static void AddReports(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<AsteroidReport> reports)
    {
        using var command = Sql.Prepare(connection, transaction,
            $"""
            INSERT INTO asteroid_reports ({ReportColumns})
            VALUES ($id, $time, $g, $s, $p, $kind, $available, $metal, $crystal, $deuterium, $dark_matter)
            ON CONFLICT (id) DO NOTHING
            """);

        foreach (var r in reports)
        {
            command.Set("$id", r.Id).Set("$time", Sql.Ticks(r.Time)).Set("$g", r.Target.Galaxy).Set("$s", r.Target.System).Set("$p", r.Target.Position)
                .Set("$kind", (int)r.Kind).Set("$available", r.Available)
                .Set("$metal", r.Gained.Metal).Set("$crystal", r.Gained.Crystal).Set("$deuterium", r.Gained.Deuterium).Set("$dark_matter", r.DarkMatter)
                .ExecuteNonQuery();
        }
    }
}
