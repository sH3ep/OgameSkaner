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
