namespace OgameSkaner.Core.Domain.Buildings;

public enum RuleMode
{
    /// <summary>Target = reference level + value (value may be negative).</summary>
    Relative,

    /// <summary>Target = value.</summary>
    Absolute
}

/// <summary>How far one building should be upgraded.</summary>
public sealed record BuildingRule(BuildingType Building, bool Enabled, RuleMode Mode, int Value)
{
    public int TargetFor(int referenceLevel) =>
        Math.Max(0, Mode == RuleMode.Relative ? referenceLevel + Value : Value);
}

/// <summary>
/// What to build on every planet. The solar plant and the storages have no rules of their own: the solar plant
/// is upgraded when the planet would run out of energy (up to <see cref="SolarMaxLevel"/>), a storage when its
/// resource is at <see cref="StorageFillPercent"/>% of capacity or more (0 turns that off).
/// </summary>
public sealed record BuildStrategy(int ReferenceLevel, IReadOnlyList<BuildingRule> Rules, int SolarMaxLevel, int StorageFillPercent = 70)
{
    /// <summary>Buildings planned automatically instead of by a rule.</summary>
    public static bool IsAutomatic(BuildingType building) =>
        building is BuildingType.SolarPlant or BuildingType.MetalStorage or BuildingType.CrystalStorage or BuildingType.DeuteriumTank;
}

public enum BuildAutomationMode
{
    /// <summary>Fill the free queue slots once; a planet stops at the first item the server refuses.</summary>
    Single,

    /// <summary>Keep adding affordable upgrades, round-robin over planets, until every planet is done.</summary>
    Auto
}
