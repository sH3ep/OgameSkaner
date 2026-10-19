using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Domain.Moons;

/// <summary>The moon formation page of one planet (<c>/home/moonformation</c>).</summary>
/// <param name="CurrentDiameter">The planet's moon's diameter; 0 when it has no moon yet.</param>
/// <param name="Metal">The planet's metal, which pays for the moon.</param>
public sealed record MoonFormationInfo(PlanetId Planet, int GravitonLevel, int CurrentDiameter, int MinDiameter, int MaxDiameter, long Metal)
{
    public bool HasMoon => CurrentDiameter > 0;

    /// <summary>The largest moon the account's Graviton level allows.</summary>
    public int LargestAllowed => Math.Min(MaxDiameter, MoonFormation.MaxDiameterFor(GravitonLevel));
}

/// <summary>
/// The game's moon creation rules (<c>/assets/js/app/pages/moonFormation.js</c>): the size (diameter) decides the metal
/// cost and the Graviton Research level needed.
/// </summary>
public static class MoonFormation
{
    public const int MinDiameter = 1_000;

    public const int MaxDiameter = 9_999;

    /// <summary><c>CalculateMoonCostMetal</c>: 17,311,270 metal for the smallest moon.</summary>
    public static long MetalCost(int diameter)
    {
        double d = diameter;
        return (long)Math.Ceiling(2 * d * (d - 900) * ((d + 10_000) / 10_000) * Math.Sqrt(d) * Math.Pow(1.2, d / 200));
    }

    /// <summary><c>CalculateGravitonResearchLevel</c>: none up to 1,799 km, then one level per 600 km.</summary>
    public static int GravitonNeeded(int diameter) => Math.Max(0, (int)Math.Ceiling((diameter - 1_799) / 600.0));

    /// <summary>The largest diameter <paramref name="gravitonLevel"/> allows (4 → 4,199 km).</summary>
    public static int MaxDiameterFor(int gravitonLevel) => Math.Min(MaxDiameter, 1_799 + 600 * Math.Max(0, gravitonLevel));

    /// <summary><paramref name="diameter"/> within the game's range and what <paramref name="gravitonLevel"/> allows.</summary>
    public static int Clamp(int diameter, int gravitonLevel) => Math.Clamp(diameter, MinDiameter, Math.Max(MinDiameter, MaxDiameterFor(gravitonLevel)));
}
