using Microsoft.Data.Sqlite;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>Reads and writes the <c>action_history</c> table.</summary>
internal static class ActionHistoryTables
{
    public static void Add(SqliteConnection connection, ActionGroupInfo group)
    {
        using var command = Sql.Prepare(connection, null,
            """
            INSERT INTO action_history (owner, description, priority, state, queued_at, started_at, finished_at, wait_ms, run_ms, error)
            VALUES ($owner, $description, $priority, $state, $queued, $started, $finished, $wait, $run, $error)
            """);
        var finished = group.FinishedAt ?? group.StartedAt ?? group.QueuedAt;
        command.Set("$owner", group.Owner).Set("$description", group.Description)
            .Set("$priority", group.Priority.ToString()).Set("$state", group.State.ToString())
            .Set("$queued", Sql.Ticks(group.QueuedAt))
            .Set("$started", group.StartedAt is { } started ? Sql.Ticks(started) : null)
            .Set("$finished", Sql.Ticks(finished))
            .Set("$wait", group.StartedAt is { } s ? (long)(s - group.QueuedAt).TotalMilliseconds : null)
            .Set("$run", group.StartedAt is { } r ? (long)(finished - r).TotalMilliseconds : null)
            .Set("$error", group.Error)
            .ExecuteNonQuery();
    }

    /// <summary>The groups finished at or after <paramref name="since"/>, newest first.</summary>
    public static IReadOnlyList<ActionGroupInfo> Load(SqliteConnection connection, DateTimeOffset since)
    {
        using var command = Sql.Prepare(connection, null,
            """
            SELECT id, owner, description, priority, state, queued_at, started_at, finished_at, error
            FROM action_history WHERE finished_at >= $since ORDER BY finished_at DESC, id DESC
            """);
        command.Set("$since", Sql.Ticks(since));
        var groups = new List<ActionGroupInfo>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(new ActionGroupInfo(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                Enum.TryParse<ActionPriority>(reader.GetString(3), out var priority) ? priority : ActionPriority.Automation,
                Sql.Time(reader.GetInt64(5)))
            {
                State = Enum.TryParse<ActionGroupState>(reader.GetString(4), out var state) ? state : ActionGroupState.Done,
                StartedAt = reader.NullableInt64(6) is { } started ? Sql.Time(started) : null,
                FinishedAt = Sql.Time(reader.GetInt64(7)),
                Error = reader.NullableString(8)
            });
        }

        return groups;
    }

    /// <summary>Deletes the groups finished before <paramref name="before"/>; returns how many.</summary>
    public static int DeleteBefore(SqliteConnection connection, DateTimeOffset before)
    {
        using var command = Sql.Prepare(connection, null, "DELETE FROM action_history WHERE finished_at < $before");
        return command.Set("$before", Sql.Ticks(before)).ExecuteNonQuery();
    }
}
