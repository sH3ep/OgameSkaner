using System.Collections.Concurrent;
using OgameSkaner.Core.Services.Scheduling;
using OgameSkaner.Core.Storage.Sqlite;

namespace OgameSkaner.Core.Storage;

/// <summary>Every finished action queue group per universe, kept <see cref="SqliteActionHistoryStore.KeptFor"/> for analysis.</summary>
public interface IActionHistoryStore
{
    /// <summary>Stores a finished group (done, failed or cancelled).</summary>
    void Add(string universe, ActionGroupInfo group);

    /// <summary>The groups finished at or after <paramref name="since"/>, newest first.</summary>
    IReadOnlyList<ActionGroupInfo> Load(string universe, DateTimeOffset since);
}

/// <summary>
/// The <c>action_history</c> table of the universe's database. Rows older than <see cref="KeptFor"/> are deleted on the
/// first write of a run and then once a day.
/// </summary>
public sealed class SqliteActionHistoryStore(UniverseDatabases databases, TimeProvider timeProvider) : IActionHistoryStore
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    private static readonly TimeSpan PruneInterval = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _prunedAt = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string universe, ActionGroupInfo group)
    {
        using var connection = databases.Open(universe);
        var now = timeProvider.GetUtcNow();
        if (!_prunedAt.TryGetValue(universe, out var pruned) || now - pruned >= PruneInterval)
        {
            ActionHistoryTables.DeleteBefore(connection, now - KeptFor);
            _prunedAt[universe] = now;
        }

        ActionHistoryTables.Add(connection, group);
    }

    public IReadOnlyList<ActionGroupInfo> Load(string universe, DateTimeOffset since)
    {
        using var connection = databases.Open(universe);
        return ActionHistoryTables.Load(connection, since);
    }
}
