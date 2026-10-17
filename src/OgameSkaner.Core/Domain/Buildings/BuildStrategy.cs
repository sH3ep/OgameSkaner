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
/// resource is at <see cref="StorageFillPercent"/>% of capacity or more (0 turns that off). The simulation keeps the
/// mines level (<see cref="MineBalancePercent"/>, see <see cref="Optimizer.OptimizerSetup.MineBalancePercent"/>) and builds
/// Robotics <see cref="EarlyRoboticsLevel"/> before a mine passes <see cref="EarlyRoboticsMineLevel"/>.
/// </summary>
public sealed record BuildStrategy(
    int ReferenceLevel,
    IReadOnlyList<BuildingRule> Rules,
    int SolarMaxLevel,
    int StorageFillPercent = 70,
    int MineBalancePercent = BuildStrategy.DefaultMineBalancePercent,
    int EarlyRoboticsLevel = BuildStrategy.DefaultEarlyRoboticsLevel,
    int EarlyRoboticsMineLevel = BuildStrategy.DefaultEarlyRoboticsMineLevel)
{
    public const int DefaultMineBalancePercent = 30;
    public const int DefaultEarlyRoboticsLevel = 6;
    public const int DefaultEarlyRoboticsMineLevel = 10;

    /// <summary>Buildings planned automatically instead of by a rule.</summary>
    public static bool IsAutomatic(BuildingType building) =>
        building is BuildingType.SolarPlant or BuildingType.MetalStorage or BuildingType.CrystalStorage or BuildingType.DeuteriumTank;
}
