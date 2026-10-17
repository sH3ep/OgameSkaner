namespace OgameSkaner.Core.Domain.Raiding;

/// <summary>The player rankings the app reads (<c>SubCategory</c> of <c>/statistics/statisticsdata</c>).</summary>
public enum RankingCategory
{
    /// <summary>Points of the defenses (1 point per 1,000 resources): 0 means no defenses worth mentioning.</summary>
    Defense
}

/// <summary>One row of a ranking.</summary>
public sealed record RankingEntry(int Rank, string PlayerId, string PlayerName, long Points, PlayerStatus Status);

/// <summary>One page of a ranking (100 players) and how many pages the ranking has.</summary>
public sealed record RankingPage(IReadOnlyList<RankingEntry> Entries, int PageCount);

/// <summary>A whole ranking as read at one time: the points by player id.</summary>
public sealed record RankingSnapshot(DateTimeOffset? ReadAt, IReadOnlyDictionary<string, long> PointsByPlayerId)
{
    public static RankingSnapshot Empty { get; } = new(null, new Dictionary<string, long>());

    public long? PointsOf(string? playerId) =>
        playerId is not null && PointsByPlayerId.TryGetValue(playerId, out var points) ? points : null;
}
