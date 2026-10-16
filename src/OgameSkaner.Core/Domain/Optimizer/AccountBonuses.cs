namespace OgameSkaner.Core.Domain.Optimizer;

/// <summary>
/// Increases of one resource as the game's Resources page lists them, as fractions of the mine's base production
/// (0.3 = +30%). They add up ("increased"); the class bonus is applied on top ("more").
/// </summary>
public sealed record ProductionBonus(double Research, double Officers, double Academy, double Premium, double Ranking)
{
    public static ProductionBonus None { get; } = new(0, 0, 0, 0, 0);

    public double Total => Research + Officers + Academy + Premium + Ranking;

    public override string ToString() => FormattableString.Invariant(
        $"+{Total:P0} (research {Research:P0}, officers {Officers:P0}, academy {Academy:P0}, premium {Premium:P0}, ranking {Ranking:P0})");
}

/// <summary>
/// The account's research, officers, academy, premium, ranking and class bonuses. Read-only for us: read from the
/// game before every simulation (<c>IOgamexClient.GetAccountBonusesAsync</c>) and fed into <see cref="GameModel"/>.
/// </summary>
/// <remarks>
/// Production of a resource per hour = (basic income + mine · energy factor · (1 + Σ increases)) · (1 + class bonus),
/// where the mine's base already contains the economy speed and the planet slot's bonus. Speeds work like the
/// Nanite Factory's "+80% production speed": time ÷ (1 + Σ speed bonuses).
/// </remarks>
public sealed record AccountBonuses
{
    /// <summary>Economy speed of the universe (basic metal income is 30 × speed per hour).</summary>
    public double EconomySpeed { get; init; } = 2000;

    public ProductionBonus Metal { get; init; } = ProductionBonus.None;
    public ProductionBonus Crystal { get; init; } = ProductionBonus.None;
    public ProductionBonus Deuterium { get; init; } = ProductionBonus.None;

    /// <summary>Energy production increase (energy technology, engineer, academy, premium).</summary>
    public ProductionBonus Energy { get; init; } = ProductionBonus.None;

    /// <summary>Class bonus on all production, applied after the increases (Miner: 0.5).</summary>
    public double ClassBonus { get; init; }

    public string ClassName { get; init; } = string.Empty;

    /// <summary>Construction speed: Construction expert + academy Construction mastery (+ premium).</summary>
    public double ConstructionSpeed { get; init; }

    /// <summary>Ship production speed: Technocrat + academy Shipyard mastery (+ premium).</summary>
    public double ShipyardSpeed { get; init; }

    /// <summary>Storage capacity increase: Storage expert + academy Storage mastery (+ premium).</summary>
    public double StorageIncrease { get; init; }

    /// <summary>Officer and academy levels behind the numbers, for display.</summary>
    public string Details { get; init; } = string.Empty;

    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>
    /// The test account's bonuses when the model was calibrated (genesis, 2026-10-16): durations, satellite times
    /// and storage capacities were measured with these speeds, so they are divided out to get the base values.
    /// </summary>
    public static AccountBonuses Calibration { get; } = new()
    {
        EconomySpeed = 2000,
        Metal = new ProductionBonus(0.04, 0.30, 0.55, 0, 5.0),
        Crystal = new ProductionBonus(0.04, 0.30, 0.55, 0, 5.0),
        Deuterium = new ProductionBonus(0.04, 0.30, 0.55, 0, 5.0),
        Energy = new ProductionBonus(0.16, 0.06, 0.08, 0, 0),
        ClassBonus = 0.5,
        ClassName = "Miner",
        ConstructionSpeed = 0.8,
        ShipyardSpeed = 0.1,
        StorageIncrease = 0.1,
        Details = "Values of the test account on 2026-10-16 (not read from the game yet)."
    };

    /// <summary>Everything that changes the simulation, rounded; part of the setup key.</summary>
    public string Signature => FormattableString.Invariant(
        $"speed {EconomySpeed:0} m {Metal.Total:0.###} c {Crystal.Total:0.###} d {Deuterium.Total:0.###} e {Energy.Total:0.###} class {ClassBonus:0.###} build {ConstructionSpeed:0.###} ships {ShipyardSpeed:0.###} storage {StorageIncrease:0.###}");

    public override string ToString() => FormattableString.Invariant(
        $"Metal {Metal}\nCrystal {Crystal}\nDeuterium {Deuterium}\nEnergy {Energy}\nClass {ClassName} +{ClassBonus:P0} on everything · construction speed +{ConstructionSpeed:P0} · ship production +{ShipyardSpeed:P0} · storage +{StorageIncrease:P0} · economy speed {EconomySpeed:0}");
}
