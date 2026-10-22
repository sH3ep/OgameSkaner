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

    /// <summary>
    /// Attacks sent to one planet on one report: another one goes while what the attacks before it leave still brings
    /// <see cref="MinLoot"/> (with 50% loot, 2 attacks from 2× min loot, 3 from 4×). 1 = one attack per report.
    /// </summary>
    public int MaxAttacksPerTarget { get; init; } = 4;

    /// <summary>The single cargo ship of older settings; only used while <see cref="CargoShipTypes"/> is empty.</summary>
    public string CargoShipType { get; init; } = "HEAVY_CARGO";

    /// <summary>
    /// The cargo ships attacks may use, highest priority first: an attack takes the first type as far as the planet has
    /// it and tops up with the next ones. Empty = <see cref="CargoShipType"/> alone (settings saved before the list).
    /// </summary>
    public IReadOnlyList<string> CargoShipTypes { get; init; } = [];

    /// <summary><see cref="CargoShipTypes"/>, or <see cref="CargoShipType"/> when that is empty.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> CargoShipPriority => CargoShipTypes.Count > 0 ? CargoShipTypes : [CargoShipType];

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

    /// <summary>
    /// Send each attack and spy from the nearest planet of <see cref="OriginPlanetIds"/> that has the ships, instead of
    /// the current planet; <see cref="NearestTargetsFirst"/> then measures from the nearest of them.
    /// </summary>
    public bool UseNearestPlanet { get; init; }

    /// <summary>The planets (ids) fleets may leave from with <see cref="UseNearestPlanet"/>; empty = none.</summary>
    public IReadOnlyList<string> OriginPlanetIds { get; init; } = [];

    /// <summary>
    /// The raider attacks the targets with the most loot per flight time first (loot ÷ √distance from the nearest
    /// origin: a fleet slot brings in the most per hour) and spies on them by the loot they likely hold now the same way
    /// (<see cref="Services.Raiding.RaidPlanner.EstimateLoot"/>). Off = by loot alone, however far.
    /// </summary>
    public bool NearestTargetsFirst { get; init; } = true;

    /// <summary>The days of spy reports the target statistics look at (Farm stats page, top targets, low-resource bans).</summary>
    public int StatsDays { get; init; } = 14;

    /// <summary>
    /// The best share of the targets by the statistics' score (<see cref="Services.Raiding.TargetStats.Score"/>): the
    /// raider spies on them before the others and re-spies a low-loot one after <see cref="TopRespyHours"/>. 0 = off.
    /// </summary>
    public int TopTargetsPercent { get; init; } = 20;

    /// <summary>A top target's <see cref="LowLootRespyHours"/>.</summary>
    public int TopRespyHours { get; init; } = 1;

    /// <summary>
    /// Reports in a row with loot below <see cref="MinLoot"/> (and too slow a refill) that mark a planet low resource,
    /// so it isn't spied on for <see cref="LowResourceBanDays"/>; after a ban one more such report renews it for
    /// <see cref="LowResourceRepeatBanDays"/>. 0 = off.
    /// </summary>
    public int LowResourceMinReports { get; init; } = 3;

    public int LowResourceBanDays { get; init; } = 3;

    public int LowResourceRepeatBanDays { get; init; } = 7;

    /// <summary>
    /// A report with loot of at least this many times <see cref="MinLoot"/> marks the planet high value: spied on right
    /// after the top targets and, with little loot, again after <see cref="TopRespyHours"/>. 0 = off.
    /// </summary>
    public int HighValueLootFactor { get; init; } = 5;

    /// <summary>Later reports in a row with loot below <see cref="MinLoot"/> that end the mark (it was a one-time stock).</summary>
    public int HighValueDropReports { get; init; } = 2;

    /// <summary>Do everything except sending fleets: log what would be sent. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    [JsonIgnore]
    public TimeSpan ReportMaxAge => TimeSpan.FromMinutes(Math.Max(1, ReportMaxAgeMinutes));

    public static RaidSettings Default { get; } = new();
}
