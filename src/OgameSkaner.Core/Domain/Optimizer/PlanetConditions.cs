namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>
/// What a planet's slot and temperature change: production bonuses per slot (OgameX wiki, "Planet Slots /
/// Colonization"), deuterium production (average temperature) and satellite energy (minimum temperature).
/// </summary>
public sealed record PlanetConditions(int Slot, int MinTemperature, int MaxTemperature)
{
    // Slot → (lowest, highest temperature of the slot's range, metal, crystal, deuterium bonus).
    private static readonly (int Low, int High, double Metal, double Crystal, double Deuterium)[] Slots =
    [
        (75, 95, 0, 0.34, 0), (70, 90, 0, 0.32, 0), (65, 85, 0, 0.30, 0),
        (60, 80, 0.15, 0.15, 0), (50, 70, 0.15, 0.15, 0), (40, 60, 0.15, 0.15, 0),
        (30, 50, 0.30, 0, 0), (20, 40, 0.30, 0, 0), (10, 30, 0.30, 0, 0),
        (0, 20, 0.15, 0, 0.10), (-10, 10, 0.15, 0, 0.10), (-20, 0, 0.15, 0, 0.10),
        (-30, -10, 0, 0, 0.20), (-40, -20, 0, 0, 0.20), (-50, -30, 0, 0, 0.20)
    ];

    public const int SlotCount = 15;

    public double MetalBonus => Slots[Index].Metal;
    public double CrystalBonus => Slots[Index].Crystal;
    public double DeuteriumBonus => Slots[Index].Deuterium;

    public double AverageTemperature => (MinTemperature + MaxTemperature) / 2.0;

    private int Index => Math.Clamp(Slot, 1, SlotCount) - 1;

    /// <summary>A typical planet in the slot: the middle of its range, ±2 °C (real planets: Colony 69–74 °C, Home 74–75 °C).</summary>
    public static PlanetConditions TypicalFor(int slot)
    {
        var (low, high, _, _, _) = Slots[Math.Clamp(slot, 1, SlotCount) - 1];
        var middle = (low + high) / 2;
        return new PlanetConditions(slot, middle - 2, middle + 2);
    }

    public string Description => FormattableString.Invariant(
        $"slot {Slot}: metal +{MetalBonus:P0}, crystal +{CrystalBonus:P0}, deuterium +{DeuteriumBonus:P0}, {MinTemperature}–{MaxTemperature} °C");
}
