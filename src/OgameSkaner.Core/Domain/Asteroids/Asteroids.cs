using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Asteroids;

/// <summary>
/// One row of the galaxy's "Find asteroids" hint (<c>/galaxy/Partial_AsteroidLocation</c>): an asteroid may be at
/// position 17 of a system between <see cref="FromSystem"/> and <see cref="ToSystem"/>.
/// </summary>
public sealed record AsteroidHint(int Galaxy, int FromSystem, int ToSystem)
{
    public bool Contains(Coordinates coordinates) =>
        coordinates.Galaxy == Galaxy && coordinates.System >= FromSystem && coordinates.System <= ToSystem;

    public override string ToString() => $"{Galaxy}:{FromSystem}–{ToSystem}";
}

/// <summary>An asteroid seen in the galaxy view (always at position 17).</summary>
public sealed record Asteroid(Coordinates Coordinates, DateTimeOffset DisappearsAt, DateTimeOffset SeenAt);

public enum AsteroidResultKind
{
    Nothing,
    Resources,
    DarkMatter,
    Container
}

/// <summary>The message the game sends about one asteroid mining mission.</summary>
/// <param name="Available">Resources the asteroid held (the miners bring at most their cargo capacity); 0 when the report shows none.</param>
public sealed record AsteroidReport(
    string Id,
    DateTimeOffset Time,
    Coordinates Target,
    AsteroidResultKind Kind,
    long Available,
    ResourceSet Gained,
    long DarkMatter);

/// <summary>Asteroid Miners the runner sent.</summary>
/// <param name="RecalledAt">When the player said they turned the fleet back (the asteroid can get miners again); null = not recalled.</param>
public sealed record AsteroidMission(
    Coordinates Target,
    Coordinates Origin,
    long Miners,
    long EstimatedResources,
    DateTimeOffset SentAt,
    DateTimeOffset ArrivesAt,
    DateTimeOffset ReturnsAt,
    DateTimeOffset? RecalledAt = null);

/// <summary>One solar system of the galaxy view: the players' planets and the asteroid at position 17, if any.</summary>
/// <param name="Debris">The debris fields per position (position 16 too); empty when there are none.</param>
public sealed record GalaxySystemView(IReadOnlyList<PlayerPlanet> Planets, Asteroid? Asteroid, IReadOnlyDictionary<int, ResourceSet>? Debris = null)
{
    public ResourceSet DebrisAt(int position) => Debris?.GetValueOrDefault(position) ?? ResourceSet.Zero;
}
