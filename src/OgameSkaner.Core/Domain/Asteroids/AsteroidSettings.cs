using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Domain.Asteroids;

/// <summary>The Asteroids page's options, saved per universe.</summary>
public sealed record AsteroidSettings
{
    /// <summary>Miners for this much more than the estimated resources.</summary>
    public int MarginPercent { get; init; } = 20;

    /// <summary>Miners sent while no mining report gives an estimate yet.</summary>
    public long MinersWithoutEstimate { get; init; } = 1_000_000;

    /// <summary>At least this many miners per asteroid.</summary>
    public long MinMiners { get; init; } = 1;

    /// <summary>At most this many miners per asteroid (null or 0 = no limit); wins over the other counts.</summary>
    public long? MaxMiners { get; init; }

    /// <summary>
    /// Miners aren't sent when their cargo holds less than this share of the estimated resources (too few miners on the
    /// planet or <see cref="MaxMiners"/>); 0 = always send. Without an estimate there's nothing to compare.
    /// </summary>
    public int MinCoveragePercent { get; init; } = 70;

    /// <summary>The miners must arrive at least this long before the asteroid disappears.</summary>
    public int ArrivalSafetySeconds { get; init; } = 60;

    /// <summary>Share of the account's fleet slots mining fleets may fill.</summary>
    public int FleetSlotUsePercent { get; init; } = 90;

    /// <summary>The runner looks for new asteroids at least this often.</summary>
    public int CheckIntervalMinutes { get; init; } = 10;

    public int SpeedPercentage { get; init; } = 100;

    /// <summary>Do everything except sending fleets: log what would be sent. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    public static AsteroidSettings Default { get; } = new();
}
