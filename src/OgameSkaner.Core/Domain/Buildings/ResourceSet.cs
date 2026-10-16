namespace OgameSkaner.Core.Domain.Buildings;

/// <summary>An amount of metal, crystal and deuterium (resources on a planet or the cost of an upgrade).</summary>
public readonly record struct ResourceSet(long Metal, long Crystal, long Deuterium)
{
    public static ResourceSet Zero => default;

    public const double MetalWeight = 1;
    public const double CrystalWeight = 1.5;
    public const double DeuteriumWeight = 3;

    /// <summary>Worth in metal units (crystal ×1.5, deuterium ×3); used to find the cheapest upgrade.</summary>
    public double Value => Metal * MetalWeight + Crystal * CrystalWeight + Deuterium * DeuteriumWeight;

    /// <summary>Every amount multiplied by <paramref name="factor"/>, rounded like the game (solar plant 8 = 1,708.6 → 1,709).</summary>
    public ResourceSet Scale(double factor) => new(Round(Metal * factor), Round(Crystal * factor), Round(Deuterium * factor));

    private static long Round(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    public bool CanAfford(ResourceSet cost) =>
        Metal >= cost.Metal && Crystal >= cost.Crystal && Deuterium >= cost.Deuterium;

    public static ResourceSet operator +(ResourceSet a, ResourceSet b) =>
        new(a.Metal + b.Metal, a.Crystal + b.Crystal, a.Deuterium + b.Deuterium);

    public static ResourceSet operator -(ResourceSet a, ResourceSet b) =>
        new(a.Metal - b.Metal, a.Crystal - b.Crystal, a.Deuterium - b.Deuterium);

    public override string ToString() => $"M {Metal:N0} / C {Crystal:N0} / D {Deuterium:N0}";
}
