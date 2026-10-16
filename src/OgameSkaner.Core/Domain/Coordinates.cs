namespace OgameSkaner.Core.Domain;

/// <summary>Position of a planet in the universe (galaxy:system:position).</summary>
public readonly record struct Coordinates(int Galaxy, int System, int Position)
{
    public SolarSystemAddress SolarSystem => new(Galaxy, System);

    public override string ToString() => $"{Galaxy}:{System}:{Position}";
}

/// <summary>Address of a single solar system (galaxy:system).</summary>
public readonly record struct SolarSystemAddress(int Galaxy, int System)
{
    public override string ToString() => $"{Galaxy}:{System}";
}
