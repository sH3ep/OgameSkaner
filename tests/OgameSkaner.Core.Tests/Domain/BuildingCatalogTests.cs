using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Tests.Domain;

public class BuildingCatalogTests
{
    [Fact]
    public void Every_building_type_has_a_definition() =>
        Assert.Equal(Enum.GetValues<BuildingType>(), BuildingCatalog.All.Select(d => d.Type));

    [Fact]
    public void Server_ids_map_both_ways()
    {
        Assert.All(BuildingCatalog.All, d => Assert.True(BuildingCatalog.TryGetByServerId(d.ServerId, out var t) && t == d.Type));
        Assert.False(BuildingCatalog.TryGetByServerId("MOON_BASE", out _));
    }

    // Values as shown by the game on 2026-10-16.
    [Theory]
    [InlineData(BuildingType.SolarPlant, 8, 1709, 683, 0)]
    [InlineData(BuildingType.SolarPlant, 9, 2563, 1025, 0)]
    [InlineData(BuildingType.MetalStorage, 6, 151_875, 0, 0)]
    [InlineData(BuildingType.CrystalStorage, 5, 101_250, 50_625, 0)]
    [InlineData(BuildingType.DeuteriumTank, 4, 67_500, 67_500, 0)]
    [InlineData(BuildingType.RoboticsFactory, 11, 409_600, 122_880, 204_800)]
    [InlineData(BuildingType.Shipyard, 12, 819_200, 409_600, 204_800)]
    [InlineData(BuildingType.ResearchLab, 9, 51_200, 102_400, 51_200)]
    [InlineData(BuildingType.MissileSilo, 1, 20_000, 20_000, 1_000)]
    [InlineData(BuildingType.FusionReactor, 1, 900, 360, 180)]
    public void Cost_matches_the_game(BuildingType building, int level, long metal, long crystal, long deuterium) =>
        Assert.Equal(new ResourceSet(metal, crystal, deuterium), BuildingCatalog.CostOf(building, level));

    [Fact]
    public void Energy_formulas_match_known_values()
    {
        Assert.Equal(11, EnergyFormulas.ConsumptionOf(BuildingType.MetalMine, 1));
        Assert.Equal(25, EnergyFormulas.ConsumptionOf(BuildingType.MetalMine, 2));
        Assert.Equal(22, EnergyFormulas.ConsumptionOf(BuildingType.DeuteriumSynthesizer, 1));
        Assert.Equal(0, EnergyFormulas.ConsumptionOf(BuildingType.Shipyard, 5));
        Assert.Equal(22, EnergyFormulas.SolarProduction(1));
        Assert.Equal(48, EnergyFormulas.SolarProduction(2));
    }

    [Fact]
    public void Projected_balance_adds_only_the_planned_differences_to_the_games_balance()
    {
        var built = new Dictionary<BuildingType, int> { [BuildingType.SolarPlant] = 1, [BuildingType.MetalMine] = 1 };
        var planned = new Dictionary<BuildingType, int> { [BuildingType.SolarPlant] = 2, [BuildingType.MetalMine] = 2, [BuildingType.CrystalMine] = 1 };

        // solar +26 (22 → 48), metal −14 (11 → 25), crystal −11
        Assert.Equal(100 + 26 - 14 - 11, EnergyFormulas.Projected(100, built, planned));
    }

    [Theory]
    [InlineData(RuleMode.Relative, 3, 13)]
    [InlineData(RuleMode.Relative, -2, 8)]
    [InlineData(RuleMode.Relative, -20, 0)]
    [InlineData(RuleMode.Absolute, 5, 5)]
    public void Rule_target_uses_the_reference_level(RuleMode mode, int value, int expected) =>
        Assert.Equal(expected, new BuildingRule(BuildingType.MetalMine, true, mode, value).TargetFor(10));
}
