namespace OgameSkaner.Core.Domain;

/// <summary>
/// A rectangular range of solar systems to scan: systems
/// <see cref="StartSystem"/>..<see cref="EndSystem"/> in each galaxy from
/// <see cref="StartGalaxy"/> to <see cref="EndGalaxy"/>.
/// </summary>
public sealed record ScanRange(int StartGalaxy, int StartSystem, int EndGalaxy, int EndSystem)
{
    public int Count => Math.Max(0, EndGalaxy - StartGalaxy + 1) * Math.Max(0, EndSystem - StartSystem + 1);

    public IReadOnlyList<string> Validate(UniverseProfile universe)
    {
        var errors = new List<string>();

        if (StartGalaxy < 1 || EndGalaxy > universe.Galaxies)
            errors.Add($"Galaxy must be between 1 and {universe.Galaxies}.");
        if (StartSystem < 1 || EndSystem > universe.Systems)
            errors.Add($"System must be between 1 and {universe.Systems}.");
        if (StartGalaxy > EndGalaxy)
            errors.Add("Start galaxy must not be greater than end galaxy.");
        if (StartSystem > EndSystem)
            errors.Add("Start system must not be greater than end system.");

        return errors;
    }

    public IEnumerable<SolarSystemAddress> Enumerate()
    {
        for (var galaxy = StartGalaxy; galaxy <= EndGalaxy; galaxy++)
        for (var system = StartSystem; system <= EndSystem; system++)
            yield return new SolarSystemAddress(galaxy, system);
    }
}
