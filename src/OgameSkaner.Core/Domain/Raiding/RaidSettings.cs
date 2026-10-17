using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Domain.Raiding;

/// <summary>The Raids page's options, saved per universe.</summary>
public sealed record RaidSettings
{
    /// <summary>Share of the account's fleet slots attacks may fill; spies may use every slot but one.</summary>
    public int FleetSlotUsePercent { get; init; } = 90;

    /// <summary>A target is attacked only when its defenses are worth at most this many points (as the report shows them).</summary>
    public long MaxDefensePoints { get; init; }

    /// <summary>A target is attacked only when its ships are worth at most this many points (as the report shows them).</summary>
    public long MaxFleetPoints { get; init; }

    /// <summary>Attacks carrying less than this (metal + crystal + deuterium) aren't sent.</summary>
    public long MinLoot { get; init; } = 1_000_000;

    /// <summary>Share of the planet's resources an attack takes.</summary>
    public int LootPercent { get; init; } = 50;

    public string CargoShipType { get; init; } = "HEAVY_CARGO";

    /// <summary>Extra cargo on top of the expected loot (production during the flight, rounding).</summary>
    public int CargoMarginPercent { get; init; } = 10;

    public int ProbeCount { get; init; } = 5;

    /// <summary>Older reports are spied again before attacking.</summary>
    public int ReportMaxAgeMinutes { get; init; } = 30;

    /// <summary>A planet whose newest report has less loot than <see cref="MinLoot"/> isn't spied again for this many hours (0 = off).</summary>
    public int LowLootRespyHours { get; init; } = 5;

    public int SpeedPercentage { get; init; } = 100;

    /// <summary>
    /// Only players with at most this many points in the defense ranking are raided (0 = only players without
    /// defenses); players not in the ranking are skipped then. Null = no limit.
    /// </summary>
    public long? MaxRankingDefensePoints { get; init; }

    /// <summary>Send each attack from the nearest own planet that has enough cargo ships, instead of the current planet.</summary>
    public bool UseNearestPlanet { get; init; }

    /// <summary>
    /// The raider spies on and attacks nearer targets first (the fleets are back sooner, so the slots are used more
    /// often): ties keep the usual order, most loot first for attacks and best ranked first for spies.
    /// </summary>
    public bool NearestTargetsFirst { get; init; } = true;

    /// <summary>Do everything except sending fleets: log what would be sent. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    [JsonIgnore]
    public TimeSpan ReportMaxAge => TimeSpan.FromMinutes(Math.Max(1, ReportMaxAgeMinutes));

    public static RaidSettings Default { get; } = new();
}
