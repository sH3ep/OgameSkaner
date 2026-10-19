using System.Text.Json.Serialization;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.Core.Domain.Moons;

public enum MoonBuildMode
{
    /// <summary>The order that reaches the targets in the shortest time (moons produce nothing, so time is all that counts).</summary>
    Simulation,

    /// <summary>The steps of <see cref="MoonSettings.SchemaSteps"/> in their order.</summary>
    Schema
}

/// <summary>The Builder's Moons tab, saved per universe.</summary>
public sealed record MoonSettings
{
    /// <summary>The moons (ids) the automation builds on; new moons aren't ticked.</summary>
    public IReadOnlyList<string> MoonIds { get; init; } = [];

    public MoonBuildMode Mode { get; init; } = MoonBuildMode.Simulation;

    /// <summary>Target level per moon building for <see cref="MoonBuildMode.Simulation"/> (missing = 0).</summary>
    public IReadOnlyList<SchemaStep> Targets { get; init; } = DefaultTargets;

    /// <summary>The build order for <see cref="MoonBuildMode.Schema"/>.</summary>
    public IReadOnlyList<SchemaStep> SchemaSteps { get; init; } = [];

    /// <summary>The cargo ships transports use, highest priority first.</summary>
    public IReadOnlyList<string> CargoShipTypes { get; init; } = ["HEAVY_CARGO", "LIGHT_CARGO"];

    /// <summary>A transport brings what this many hours of the moon's construction need, no more (moons are unguarded).</summary>
    public double HorizonHours { get; init; } = 6;

    /// <summary>The running automation sends the transports itself.</summary>
    public bool AutoTransport { get; init; } = true;

    /// <summary>The automation checks the moons at least this often.</summary>
    public int CheckIntervalMinutes { get; init; } = 10;

    /// <summary>At most this many items in a moon's game queue (the game drops an item it can't pay when its turn comes).</summary>
    public int MaxServerQueueDepth { get; init; } = 3;

    public int TransportSpeedPercent { get; init; } = 100;

    /// <summary>The size of new moons (decides their metal cost).</summary>
    public int CreateDiameter { get; init; } = MoonFormation.MinDiameter;

    /// <summary>Do everything except sending fleets and queuing buildings. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    [JsonIgnore]
    public TimeSpan Horizon => TimeSpan.FromHours(Math.Max(0.5, HorizonHours));

    /// <summary>The target of <paramref name="building"/> (0 when not set).</summary>
    public int TargetOf(BuildingType building) => Targets.Where(t => t.Building == building).Select(t => t.Level).DefaultIfEmpty(0).Max();

    public static IReadOnlyList<SchemaStep> DefaultTargets { get; } =
    [
        new(BuildingType.MoonBase, 8), new(BuildingType.RoboticsFactory, 10), new(BuildingType.NaniteFactory, 2),
        new(BuildingType.Shipyard, 4), new(BuildingType.PhalanxSensor, 4), new(BuildingType.JumpGate, 1)
    ];

    public static MoonSettings Default { get; } = new();
}
