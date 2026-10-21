using Microsoft.Data.Sqlite;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>
/// The tables of a universe's database. <c>PRAGMA user_version</c> holds the applied version; a new version adds a
/// step to <see cref="Steps"/> (the database is backed up before an upgrade).
/// </summary>
internal static class Schema
{
    private static readonly string[] Steps =
    [
        // 1: current planets and scanned systems, their history, spy reports and fleets sent.
        """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;

        CREATE TABLE planets (
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL,
            player TEXT NOT NULL, player_id TEXT, status INTEGER NOT NULL, rank INTEGER,
            moon INTEGER NOT NULL, scanned_at INTEGER NOT NULL,
            PRIMARY KEY (galaxy, system, position)) WITHOUT ROWID;

        CREATE TABLE scanned_systems (
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, scanned_at INTEGER NOT NULL,
            PRIMARY KEY (galaxy, system)) WITHOUT ROWID;

        -- A row whenever a scan saw a slot differ from before: new planet, new owner, moon, or free (player NULL).
        CREATE TABLE slot_history (
            id INTEGER PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL,
            observed_at INTEGER NOT NULL, player TEXT, player_id TEXT, moon INTEGER NOT NULL);
        CREATE INDEX slot_history_slot ON slot_history (galaxy, system, position, observed_at);

        -- player_key is 'id:<player id>', or 'name:<name>' for scans without ids.
        CREATE TABLE players (
            player_key TEXT PRIMARY KEY, player_id TEXT, name TEXT NOT NULL,
            status INTEGER NOT NULL, rank INTEGER, observed_at INTEGER NOT NULL) WITHOUT ROWID;

        -- A row whenever a player's name, status or rank changed.
        CREATE TABLE player_history (
            id INTEGER PRIMARY KEY, player_key TEXT NOT NULL, player_id TEXT, name TEXT NOT NULL,
            status INTEGER NOT NULL, rank INTEGER, observed_at INTEGER NOT NULL);
        CREATE INDEX player_history_player ON player_history (player_key, observed_at);

        CREATE TABLE espionage_reports (
            id TEXT PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL, type INTEGER NOT NULL,
            time INTEGER NOT NULL, player TEXT, player_id TEXT, status INTEGER NOT NULL,
            metal INTEGER NOT NULL, crystal INTEGER NOT NULL, deuterium INTEGER NOT NULL, counter_espionage INTEGER,
            fleet_points INTEGER, ships TEXT, defense_points INTEGER, defenses TEXT);
        CREATE INDEX espionage_reports_target ON espionage_reports (galaxy, system, position, type, time);

        CREATE TABLE raids (
            id INTEGER PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL, type INTEGER NOT NULL,
            player TEXT, mission INTEGER NOT NULL,
            origin_galaxy INTEGER NOT NULL, origin_system INTEGER NOT NULL, origin_position INTEGER NOT NULL,
            ship_type TEXT NOT NULL, ships INTEGER NOT NULL, expected_loot INTEGER NOT NULL,
            sent_at INTEGER NOT NULL, arrives_at INTEGER NOT NULL, returns_at INTEGER NOT NULL);
        CREATE INDEX raids_sent ON raids (sent_at);
        """,

        // 2: the newest player rankings (points by player id), e.g. defense points: 0 = safe to attack.
        """
        CREATE TABLE rankings (
            category TEXT NOT NULL, player_id TEXT NOT NULL, points INTEGER NOT NULL, rank INTEGER NOT NULL,
            PRIMARY KEY (category, player_id)) WITHOUT ROWID;
        """,

        // 3: asteroids seen (position 17), the miners sent to them and the mining reports.
        """
        CREATE TABLE asteroids (
            id INTEGER PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL,
            seen_at INTEGER NOT NULL, disappears_at INTEGER NOT NULL);
        CREATE INDEX asteroids_disappears ON asteroids (disappears_at);

        CREATE TABLE asteroid_missions (
            id INTEGER PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL,
            origin_galaxy INTEGER NOT NULL, origin_system INTEGER NOT NULL, origin_position INTEGER NOT NULL,
            miners INTEGER NOT NULL, estimated_resources INTEGER NOT NULL,
            sent_at INTEGER NOT NULL, arrives_at INTEGER NOT NULL, returns_at INTEGER NOT NULL);
        CREATE INDEX asteroid_missions_sent ON asteroid_missions (sent_at);

        CREATE TABLE asteroid_reports (
            id TEXT PRIMARY KEY,
            time INTEGER NOT NULL, galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL,
            kind INTEGER NOT NULL, available INTEGER NOT NULL,
            metal INTEGER NOT NULL, crystal INTEGER NOT NULL, deuterium INTEGER NOT NULL, dark_matter INTEGER NOT NULL);
        CREATE INDEX asteroid_reports_time ON asteroid_reports (time);
        """,

        // 4: miners the player turned back, so the asteroid can get miners again.
        """
        ALTER TABLE asteroid_missions ADD COLUMN recalled_at INTEGER;
        """,

        // 5: every finished action queue group, kept 30 days for analysis (priority and state as their names; wait_ms
        // and run_ms are null for a group cancelled before it started).
        """
        CREATE TABLE action_history (
            id INTEGER PRIMARY KEY,
            owner TEXT NOT NULL, description TEXT NOT NULL, priority TEXT NOT NULL, state TEXT NOT NULL,
            queued_at INTEGER NOT NULL, started_at INTEGER, finished_at INTEGER NOT NULL,
            wait_ms INTEGER, run_ms INTEGER, error TEXT);
        CREATE INDEX action_history_finished ON action_history (finished_at);
        CREATE INDEX action_history_owner ON action_history (owner, finished_at);
        """,

        // 6: planets the raider marked low resource (kept after they end: a repeated ban lasts longer).
        """
        CREATE TABLE target_bans (
            id INTEGER PRIMARY KEY,
            galaxy INTEGER NOT NULL, system INTEGER NOT NULL, position INTEGER NOT NULL, type INTEGER NOT NULL,
            banned_at INTEGER NOT NULL, until INTEGER NOT NULL, nth INTEGER NOT NULL, reason TEXT NOT NULL, lifted_at INTEGER);
        CREATE INDEX target_bans_target ON target_bans (galaxy, system, position, banned_at);
        """
    ];

    public static int Version => Steps.Length;

    public static int CurrentVersion(SqliteConnection connection) => Convert.ToInt32(Sql.Scalar(connection, "PRAGMA user_version"));

    /// <summary>Applies the steps after <paramref name="from"/>, each in its own transaction.</summary>
    public static void Upgrade(SqliteConnection connection, int from)
    {
        for (var version = from + 1; version <= Version; version++)
        {
            using var transaction = connection.BeginTransaction();
            Sql.Execute(connection, transaction, Steps[version - 1]);
            Sql.Execute(connection, transaction, $"PRAGMA user_version = {version}");
            transaction.Commit();
        }
    }
}
