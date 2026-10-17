using OgameSkaner.Core.Domain.Optimizer;

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
/// What to build on every planet. The solar plant has no rule: it is upgraded when the planet would run out of energy
/// (up to <see cref="SolarMaxLevel"/>). A storage with an enabled rule is built to the rule's target (0 = none) and
/// beyond when its resource is at <see cref="StorageFillPercent"/>% of capacity or more (0 turns that off, see
/// <see cref="FillsStorage"/>). The simulation keeps the
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

    /// <summary>
    /// Whether <see cref="StorageFillPercent"/> applies to this storage: its rule's Enabled. A storage is still built
    /// when a cost doesn't fit in it.
    /// </summary>
    public bool FillsStorage(BuildingType storage) => Rules.FirstOrDefault(r => r.Building == storage)?.Enabled ?? true;

    /// <summary>The storages <see cref="StorageFillPercent"/> applies to.</summary>
    public StorageKinds FillStorages =>
        (FillsStorage(BuildingType.MetalStorage) ? StorageKinds.Metal : 0)
        | (FillsStorage(BuildingType.CrystalStorage) ? StorageKinds.Crystal : 0)
        | (FillsStorage(BuildingType.DeuteriumTank) ? StorageKinds.Deuterium : 0);

    public static bool IsStorage(BuildingType building) =>
        building is BuildingType.MetalStorage or BuildingType.CrystalStorage or BuildingType.DeuteriumTank;
}
