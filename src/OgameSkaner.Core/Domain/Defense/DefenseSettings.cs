using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Domain.Defense;

/// <summary>Where the fleet of an attacked planet goes.</summary>
public enum FleetEscapeMode
{
    /// <summary>Only notify.</summary>
    Off,

    /// <summary>Deploy to the planet's moon (to the nearest planet when it has none and the fallback is on).</summary>
    Moon,

    /// <summary>Deploy to the nearest other own planet.</summary>
    NearestPlanet
}

/// <summary>The Defense page's options, saved per universe.</summary>
public sealed record DefenseSettings
{
    /// <summary>The event list is read this often; keep it below the shortest flight time an attacker needs.</summary>
    public int CheckIntervalSeconds { get; init; } = 30;

    /// <summary>Notify about hostile spy probes too (attacks and missiles are always notified).</summary>
    public bool NotifyEspionage { get; init; } = true;

    public FleetEscapeMode EscapeMode { get; init; } = FleetEscapeMode.Moon;

    /// <summary>With <see cref="FleetEscapeMode.Moon"/>: go to the nearest planet when the planet has no moon.</summary>
    public bool FallbackToNearestPlanet { get; init; } = true;

    /// <summary>The escape flies this slowly, so the fleet stays in the air (and safe) longer.</summary>
    public int EscapeSpeedPercent { get; init; } = 10;

    /// <summary>The fleet leaves this long before the attack arrives (at once when it's detected later).</summary>
    public int EscapeSecondsBeforeArrival { get; init; } = 60;

    /// <summary>The escaping fleet takes the planet's resources along, as far as its cargo holds them.</summary>
    public bool TakeResources { get; init; } = true;

    /// <summary>
    /// Turn an escaped fleet back once no attack on its planet is listed any more (after
    /// <see cref="RecallDelaySeconds"/>), so it comes home in the time it has flown instead of finishing its slow flight.
    /// </summary>
    public bool RecallAfterAttack { get; init; } = true;

    /// <summary>How long after the last attack landed the escaped fleet is turned back.</summary>
    public int RecallDelaySeconds { get; init; } = 30;

    /// <summary>Start the protection when the app starts or the universe changes.</summary>
    public bool AutoStart { get; init; }

    /// <summary>Do everything except sending fleets: log what would be sent. Not saved.</summary>
    [JsonIgnore]
    public bool TestMode { get; init; }

    public static DefenseSettings Default { get; } = new();
}
