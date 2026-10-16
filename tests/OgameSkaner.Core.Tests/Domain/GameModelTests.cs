using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Tests.Domain;

/// <summary>Values read from the game (genesis, 2026-10-16); the model must reproduce them.</summary>
public class GameModelTests
{
    private readonly GameModel _model = new(maxTemperature: 84);

    [Theory]
    [InlineData(Gene.MetalMine, 1, 80, 20, 0)]
    [InlineData(Gene.MetalMine, 2, 118, 30, 0)]
    [InlineData(Gene.MetalMine, 14, 13_077, 3_269, 0)]
    [InlineData(Gene.MetalMine, 21, 200_639, 50_160, 0)]
    [InlineData(Gene.MetalMine, 28, 2_837_170, 709_293, 0)]
    [InlineData(Gene.MetalMine, 31, 8_738_945, 2_184_736, 0)]
    [InlineData(Gene.MetalMine, 36, 55_055_189, 13_763_795, 0)]
    [InlineData(Gene.CrystalMine, 12, 4_776, 2_388, 0)]
    [InlineData(Gene.CrystalMine, 26, 1_064_802, 532_406, 0)]
    [InlineData(Gene.DeuteriumSynthesizer, 1, 340, 100, 0)]
    [InlineData(Gene.DeuteriumSynthesizer, 25, 3_874_501, 1_139_559, 0)]
    [InlineData(Gene.SolarPlant, 14, 19_462, 7_785, 0)]
    [InlineData(Gene.SolarPlant, 28, 5_385_481, 2_154_202, 0)]
    [InlineData(Gene.MetalStorage, 14, 3_892_390, 0, 0)]
    [InlineData(Gene.CrystalStorage, 7, 227_813, 113_906, 0)]
    [InlineData(Gene.DeuteriumTank, 13, 2_594_927, 2_594_927, 0)]
    [InlineData(Gene.RoboticsFactory, 15, 6_553_600, 1_966_080, 3_276_800)]
    [InlineData(Gene.NaniteFactory, 2, 2_000_000, 1_000_000, 200_000)]
    [InlineData(Gene.Shipyard, 8, 51_200, 25_600, 12_800)]
    public void Costs_match_the_game(Gene gene, int level, long metal, long crystal, long deuterium)
    {
        var cost = _model.CostOf(gene, level);

        // The game's rounding differs by a few units at high levels (under 0.001%).
        Assert.InRange(cost.Metal, metal - 1 - metal / 100_000, metal + 1 + metal / 100_000);
        Assert.InRange(cost.Crystal, crystal - 1 - crystal / 100_000, crystal + 1 + crystal / 100_000);
        Assert.InRange(cost.Deuterium, deuterium - 1 - deuterium / 100_000, deuterium + 1 + deuterium / 100_000);
    }

    [Theory]
    [InlineData(Gene.MetalMine, 14, 4, 0, 13)]
    [InlineData(Gene.MetalMine, 28, 4, 0, 3_751)]
    [InlineData(Gene.MetalMine, 25, 11, 1, 186)]
    [InlineData(Gene.MetalMine, 36, 11, 1, 14_031)]
    [InlineData(Gene.CrystalMine, 26, 4, 0, 1_623)]
    [InlineData(Gene.DeuteriumSynthesizer, 13, 6, 0, 24)]
    [InlineData(Gene.DeuteriumSynthesizer, 25, 4, 0, 4_997)]
    [InlineData(Gene.SolarPlant, 28, 4, 0, 7_974)]
    [InlineData(Gene.MetalStorage, 14, 4, 0, 4_682)]
    [InlineData(Gene.DeuteriumTank, 13, 4, 0, 5_945)]
    [InlineData(Gene.RoboticsFactory, 15, 6, 0, 9_651)]
    [InlineData(Gene.RoboticsFactory, 12, 11, 1, 264)]
    [InlineData(Gene.NaniteFactory, 1, 4, 0, 1_020)]
    [InlineData(Gene.NaniteFactory, 2, 11, 1, 352)]
    [InlineData(Gene.Shipyard, 8, 6, 0, 52)]
    [InlineData(Gene.Shipyard, 12, 11, 1, 257)]
    public void Durations_match_the_game_within_half_a_percent(Gene gene, int level, int robotics, int nanite, double seconds)
    {
        var simulated = _model.Seconds(gene, level, robotics, nanite);

        Assert.InRange(simulated, Math.Floor(seconds * 0.995) - 1, Math.Ceiling(seconds * 1.005) + 1);
    }

    [Fact]
    public void Robotics_speed_up_matches_the_users_measurements()
    {
        // Metal Mine: 01:19 → 00:37 → 00:24 → 00:17 → 00:13 for Robotics 0–4 (the game shows whole seconds, floored).
        double[] shown = [79, 37, 24, 17, 13];
        for (var robotics = 1; robotics < shown.Length; robotics++)
            Assert.Equal(shown[robotics], Math.Floor(79.5 / _model.SpeedUp(robotics, 0)));
    }

    [Fact]
    public void Production_and_energy_match_the_colony()
    {
        // Colony: Metal Mine 13, Crystal Mine 15, Deuterium Synthesizer 12, Solar Plant 16 (values per second).
        Assert.Equal(8863.46, GameModel.BaseMetalIncome + _model.MetalPerSecond[13], 2);
        Assert.Equal(8239.07, GameModel.BaseCrystalIncome + _model.CrystalPerSecond[15], 2);
        Assert.Equal(1969.24, _model.DeuteriumPerSecond[12], 1);
        Assert.InRange(_model.SolarEnergy[16], 2152.6, 2152.7);
        Assert.Equal(1379.81, _model.CrystalConsumption[15] + _model.DeuteriumConsumption[12], 1);
        Assert.Equal(54.67, _model.SatelliteEnergy, 1);
    }

    [Fact]
    public void Storage_capacity_follows_the_games_table_and_keeps_growing()
    {
        Assert.Equal(11_000_000, _model.Capacity[0]);
        Assert.Equal(49_637_500, _model.Capacity[5]);
        Assert.Equal(293_510_400, _model.Capacity[11]);
        for (var level = 1; level <= GameModel.MaxLevel; level++)
            Assert.True(_model.Capacity[level] > _model.Capacity[level - 1]);
    }

    [Fact]
    public void Satellites_build_faster_with_the_shipyard()
    {
        // Shown on the hangar page for Shipyard 0–7.
        double[] shown = [23, 20, 17, 15, 13, 12, 11, 9];
        for (var shipyard = 0; shipyard < shown.Length; shipyard++)
            Assert.InRange(GameModel.SatelliteSeconds(shipyard), shown[shipyard] - 1, shown[shipyard]);
    }

    [Fact]
    public void Genes_map_to_buildings()
    {
        Assert.Equal(BuildingType.RoboticsFactory, GameModel.ToBuilding(Gene.RoboticsFactory));
        Assert.Equal(Gene.Shipyard, GameModel.ToGene(BuildingType.Shipyard));
    }
}
