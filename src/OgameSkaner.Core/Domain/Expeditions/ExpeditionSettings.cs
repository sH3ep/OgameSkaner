using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Domain.Expeditions;

/// <summary>A planet or moon that sends expeditions, and how many at a time.</summary>
/// <param name="Id">The planet's or moon's id (<c>?planet=</c>).</param>
public sealed record ExpeditionOrigin(string Id, int Count);

/// <summary>The Expeditions page's options, saved per universe.</summary>
public sealed record ExpeditionSettings
{
    /// <summary>The ship types that fly; every ship of these types at home goes, shared by the origin's expeditions.</summary>
    public IReadOnlyList<string> ShipTypes { get; init; } = [];

    /// <summary>The places that send expeditions; one not listed (or with a count of 0) sends none.</summary>
    public IReadOnlyList<ExpeditionOrigin> Origins { get; init; } = [];

    /// <summary>The stay at position 16; the nearest duration the game offers is taken.</summary>
    public int HoldMinutes { get; init; } = 60;

    public int SpeedPercent { get; init; } = 100;

    /// <summary>Fleet slots left for the other pages (raids, asteroids, escapes).</summary>
    public int KeepFleetSlotsFree { get; init; } = 1;

    /// <summary>The longest sleep between two checks (a check is due earlier when an expedition returns).</summary>
    public int CheckIntervalMinutes { get; init; } = 5;

    /// <summary>Start sending when the app starts or the universe changes.</summary>
    public bool AutoStart { get; init; }

    /// <summary>Do everything except sending fleets: log what would be sent. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    public int CountFor(string id) => Origins.FirstOrDefault(o => o.Id == id)?.Count ?? 0;

    public static ExpeditionSettings Default { get; } = new();
}
