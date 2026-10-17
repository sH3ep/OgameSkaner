using Microsoft.Data.Sqlite;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>Reads and writes the <c>planets</c> and <c>scanned_systems</c> tables and records their history.</summary>
internal static class PlanetTables
{
    public static PlanetDatabase Load(SqliteConnection connection, string universe)
    {
        var planets = new List<PlayerPlanet>();
        using (var command = Sql.Prepare(connection, null,
                   "SELECT galaxy, system, position, player, player_id, status, rank, moon, scanned_at FROM planets"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                planets.Add(new PlayerPlanet(
                    reader.GetString(3),
                    new Coordinates(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)),
                    reader.GetBoolean(7),
                    Sql.Time(reader.GetInt64(8)),
                    (PlayerStatus)reader.GetInt32(5),
                    reader.NullableString(4),
                    reader.NullableInt32(6)));
            }
        }

        var systems = new List<ScannedSystem>();
        using (var command = Sql.Prepare(connection, null, "SELECT galaxy, system, scanned_at FROM scanned_systems"))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                systems.Add(new ScannedSystem(new SolarSystemAddress(reader.GetInt32(0), reader.GetInt32(1)), Sql.Time(reader.GetInt64(2))));
        }

        return new PlanetDatabase(universe, planets, systems);
    }

    /// <summary>
    /// Replaces the stored planets of <paramref name="systems"/> with those of <paramref name="database"/>. Every slot that
    /// differs from what was stored (new planet, other owner, moon, now free) gets a <c>slot_history</c> row, every player
    /// whose name, status or rank changed a <c>player_history</c> row.
    /// </summary>
    public static void Write(SqliteConnection connection, SqliteTransaction transaction, PlanetDatabase database, IEnumerable<SolarSystemAddress> systems)
    {
        using var select = Sql.Prepare(connection, transaction,
            "SELECT position, player, player_id, moon FROM planets WHERE galaxy = $g AND system = $s");
        using var delete = Sql.Prepare(connection, transaction, "DELETE FROM planets WHERE galaxy = $g AND system = $s");
        using var insert = Sql.Prepare(connection, transaction,
            """
            INSERT INTO planets (galaxy, system, position, player, player_id, status, rank, moon, scanned_at)
            VALUES ($g, $s, $p, $player, $player_id, $status, $rank, $moon, $at)
            """);
        using var slot = Sql.Prepare(connection, transaction,
            """
            INSERT INTO slot_history (galaxy, system, position, observed_at, player, player_id, moon)
            VALUES ($g, $s, $p, $at, $player, $player_id, $moon)
            """);
        using var scanned = Sql.Prepare(connection, transaction,
            """
            INSERT INTO scanned_systems (galaxy, system, scanned_at) VALUES ($g, $s, $at)
            ON CONFLICT (galaxy, system) DO UPDATE SET scanned_at = excluded.scanned_at
            """);

        var players = new Dictionary<string, PlayerPlanet>();
        foreach (var address in systems)
        {
            var (planets, scannedAt) = database.GetSystem(address);

            var stored = new Dictionary<int, (string Player, string? PlayerId, bool Moon)>();
            select.Set("$g", address.Galaxy).Set("$s", address.System);
            using (var reader = select.ExecuteReader())
            {
                while (reader.Read())
                    stored[reader.GetInt32(0)] = (reader.GetString(1), reader.NullableString(2), reader.GetBoolean(3));
            }

            var current = planets.ToDictionary(p => p.Coordinates.Position);
            foreach (var position in stored.Keys.Union(current.Keys))
            {
                var was = stored.TryGetValue(position, out var w) ? w : ((string Player, string? PlayerId, bool Moon)?)null;
                var now = current.GetValueOrDefault(position);
                slot.Set("$g", address.Galaxy).Set("$s", address.System).Set("$p", position);

                if (now is null)
                    slot.Set("$at", Sql.Ticks(scannedAt ?? DateTimeOffset.UtcNow)).Set("$player", null).Set("$player_id", null).Set("$moon", false).ExecuteNonQuery();
                else if (was is not { } old || !SameOwner(old.Player, old.PlayerId, now) || old.Moon != now.HasMoon)
                    slot.Set("$at", Sql.Ticks(now.ScannedAt)).Set("$player", now.PlayerName).Set("$player_id", now.PlayerId).Set("$moon", now.HasMoon).ExecuteNonQuery();
            }

            delete.Set("$g", address.Galaxy).Set("$s", address.System).ExecuteNonQuery();
            foreach (var planet in planets)
            {
                insert.Set("$g", address.Galaxy).Set("$s", address.System).Set("$p", planet.Coordinates.Position)
                    .Set("$player", planet.PlayerName).Set("$player_id", planet.PlayerId).Set("$status", (int)planet.Status)
                    .Set("$rank", planet.Rank).Set("$moon", planet.HasMoon).Set("$at", Sql.Ticks(planet.ScannedAt))
                    .ExecuteNonQuery();

                var key = PlayerKey(planet.PlayerId, planet.PlayerName);
                if (!players.TryGetValue(key, out var seen) || planet.ScannedAt > seen.ScannedAt)
                    players[key] = planet;
            }

            if (scannedAt is { } at)
                scanned.Set("$g", address.Galaxy).Set("$s", address.System).Set("$at", Sql.Ticks(at)).ExecuteNonQuery();
        }

        WritePlayers(connection, transaction, players);
    }

    public static IReadOnlyList<SlotObservation> SlotHistory(SqliteConnection connection, Coordinates coordinates)
    {
        using var command = Sql.Prepare(connection, null,
            """
            SELECT observed_at, player, player_id, moon FROM slot_history
            WHERE galaxy = $g AND system = $s AND position = $p ORDER BY observed_at, id
            """);
        command.Set("$g", coordinates.Galaxy).Set("$s", coordinates.System).Set("$p", coordinates.Position);

        var history = new List<SlotObservation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            history.Add(new SlotObservation(coordinates, Sql.Time(reader.GetInt64(0)), reader.NullableString(1), reader.NullableString(2), reader.GetBoolean(3)));
        return history;
    }

    /// <summary>By id, plus what was recorded under the name before the scans had ids.</summary>
    public static IReadOnlyList<PlayerObservation> PlayerHistory(SqliteConnection connection, string? playerId, string playerName)
    {
        using var command = Sql.Prepare(connection, null,
            """
            SELECT player_id, name, observed_at, status, rank FROM player_history
            WHERE player_key IN ($id_key, $name_key) ORDER BY observed_at, id
            """);
        command.Set("$id_key", PlayerKey(playerId, playerName)).Set("$name_key", PlayerKey(null, playerName));

        var history = new List<PlayerObservation>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            history.Add(new PlayerObservation(reader.NullableString(0), reader.GetString(1), Sql.Time(reader.GetInt64(2)), (PlayerStatus)reader.GetInt32(3), reader.NullableInt32(4)));
        return history;
    }

    private static string PlayerKey(string? playerId, string playerName) =>
        string.IsNullOrEmpty(playerId) ? "name:" + playerName : "id:" + playerId;

    /// <summary>Ids decide when both scans have one (a renamed player keeps the slot); otherwise the name.</summary>
    private static bool SameOwner(string player, string? playerId, PlayerPlanet now) =>
        playerId is not null && now.PlayerId is not null ? playerId == now.PlayerId : player == now.PlayerName;

    private static void WritePlayers(SqliteConnection connection, SqliteTransaction transaction, Dictionary<string, PlayerPlanet> players)
    {
        using var select = Sql.Prepare(connection, transaction, "SELECT name, status, rank FROM players WHERE player_key = $key");
        using var upsert = Sql.Prepare(connection, transaction,
            """
            INSERT INTO players (player_key, player_id, name, status, rank, observed_at) VALUES ($key, $id, $name, $status, $rank, $at)
            ON CONFLICT (player_key) DO UPDATE SET player_id = excluded.player_id, name = excluded.name, status = excluded.status,
                rank = excluded.rank, observed_at = excluded.observed_at
            """);
        using var history = Sql.Prepare(connection, transaction,
            """
            INSERT INTO player_history (player_key, player_id, name, status, rank, observed_at)
            VALUES ($key, $id, $name, $status, $rank, $at)
            """);

        foreach (var (key, planet) in players)
        {
            (string Name, int Status, int? Rank)? stored = null;
            select.Set("$key", key);
            using (var reader = select.ExecuteReader())
            {
                if (reader.Read())
                    stored = (reader.GetString(0), reader.GetInt32(1), reader.NullableInt32(2));
            }

            // A scan without a rank (older format) doesn't forget the known one.
            var rank = planet.Rank ?? stored?.Rank;
            if (stored is { } s && s.Name == planet.PlayerName && s.Status == (int)planet.Status && s.Rank == rank)
                continue;

            foreach (var command in new[] { upsert, history })
            {
                command.Set("$key", key).Set("$id", planet.PlayerId).Set("$name", planet.PlayerName)
                    .Set("$status", (int)planet.Status).Set("$rank", rank).Set("$at", Sql.Ticks(planet.ScannedAt))
                    .ExecuteNonQuery();
            }
        }
    }
}
