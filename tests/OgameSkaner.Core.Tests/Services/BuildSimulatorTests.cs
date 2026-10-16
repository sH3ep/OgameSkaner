using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Optimizer;
using OgameSkaner.Core.Services.Optimizer;

namespace OgameSkaner.Core.Tests.Services;

public class BuildSimulatorTests
{
    private static readonly GameModel Model = new(AccountBonuses.Calibration, new PlanetConditions(8, 28, 32));

    private static BuildSimulator Simulator(OptimizerSetup setup) => new(Model, setup with { PlanetSlot = 8, MinTemperature = 28, MaxTemperature = 32 });

    private static OptimizerSetup Targets(int metal, int crystal = 0, int deuterium = 0) => new()
    {
        MetalMine = metal,
        CrystalMine = crystal,
        DeuteriumSynthesizer = deuterium,
        StartResources = ResourceSet.Zero
    };

    [Fact]
    public void Waits_for_resources_then_builds()
    {
        // Metal Mine 1 costs 80/20; base income is 25 metal and 12.5 crystal per second; it builds in 1 s.
        var seconds = Simulator(Targets(1)).Evaluate([Gene.MetalMine]);

        Assert.Equal(80 / 25.0 + 1, seconds, 6);
    }

    [Fact]
    public void Mines_without_energy_produce_only_the_base_income()
    {
        // After Metal Mine 1 (at 4.2 s, 25 metal left over from the build second) Metal Mine 2 needs 118 metal.
        var seconds = Simulator(Targets(2)).Evaluate([Gene.MetalMine, Gene.MetalMine]);

        Assert.Equal(4.2 + (118 - 25) / 25.0 + 1, seconds, 6);
    }

    [Fact]
    public void A_solar_plant_makes_the_mines_produce()
    {
        var withoutEnergy = Simulator(Targets(8)).Evaluate(Enumerable.Repeat(Gene.MetalMine, 8).ToArray());
        var withEnergy = Simulator(Targets(8)).Evaluate([Gene.SolarPlant, Gene.SolarPlant, .. Enumerable.Repeat(Gene.MetalMine, 8)]);

        Assert.True(withEnergy < withoutEnergy, $"{withEnergy} vs {withoutEnergy}");
    }

    [Fact]
    public void Robotics_shortens_the_following_constructions()
    {
        var setup = Targets(16) with { StartResources = new ResourceSet(10_000_000, 10_000_000, 1_000_000) };
        var (_, steps) = Simulator(setup).Trace([Gene.RoboticsFactory, Gene.RoboticsFactory, .. Enumerable.Repeat(Gene.MetalMine, 16)]);

        var last = steps[^1];
        Assert.Equal((Gene.MetalMine, 16), (last.Gene, last.Level));
        Assert.Equal(Model.Seconds(Gene.MetalMine, 16, robotics: 2, nanite: 0), last.FinishSeconds - last.StartSeconds, 6);
    }

    [Fact]
    public void A_cost_above_the_capacity_adds_a_storage_first()
    {
        // Metal Mine 33 costs 18.2M metal; the planet stores 11M without a metal storage.
        var setup = Targets(33) with { StartResources = new ResourceSet(10_000_000, 10_000_000, 0), SolarMaxLevel = 40 };
        var (seconds, steps) = Simulator(setup).Trace([.. Enumerable.Repeat(Gene.SolarPlant, 30), .. Enumerable.Repeat(Gene.MetalMine, 33)]);

        Assert.False(double.IsPositiveInfinity(seconds));
        var storage = steps.FindIndex(s => s.Gene == Gene.MetalStorage);
        Assert.True(storage >= 0 && steps[storage].Forced);
        Assert.True(storage < steps.FindIndex(s => s is { Gene: Gene.MetalMine, Level: 33 }));
    }

    [Fact]
    public void Satellites_need_robotics_and_shipyard_3_and_power_the_next_mine()
    {
        var setup = Targets(1) with { StartResources = new ResourceSet(100_000, 100_000, 100_000) };
        Gene[] tooEarly = [Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];
        Gene[] ready = [Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];

        var (_, skipped) = Simulator(setup).Trace(tooEarly);
        var (_, ordered) = Simulator(setup).Trace(ready);

        Assert.DoesNotContain(skipped, s => s.IsSatellites);
        var satellites = Assert.Single(ordered, s => s.IsSatellites);
        Assert.Equal(1, satellites.Level);
        Assert.Equal(Model.SatelliteSeconds(3), satellites.FinishSeconds - satellites.StartSeconds, 6);
    }

    [Fact]
    public void Satellites_are_not_ordered_without_an_energy_shortage()
    {
        var setup = Targets(1) with { StartResources = new ResourceSet(100_000, 100_000, 100_000) };
        Gene[] genes = [Gene.SolarPlant, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.Shipyard, Gene.Shipyard, Gene.Shipyard, Gene.SolarSatellites, Gene.MetalMine];

        var (_, steps) = Simulator(setup).Trace(genes);

        Assert.DoesNotContain(steps, s => s.IsSatellites);
    }

    [Fact]
    public void An_order_that_never_gets_the_resources_is_infinite()
    {
        // Robotics needs deuterium; there is none and no synthesizer.
        var seconds = Simulator(Targets(1)).Evaluate([Gene.RoboticsFactory, Gene.MetalMine]);

        Assert.True(double.IsPositiveInfinity(seconds));
    }

    [Fact]
    public void An_order_missing_target_levels_is_infinite()
    {
        Assert.True(double.IsPositiveInfinity(Simulator(Targets(3)).Evaluate([Gene.MetalMine, Gene.MetalMine])));
    }

    [Fact]
    public void Genes_above_the_max_level_are_skipped()
    {
        var setup = Targets(1) with { RoboticsMaxLevel = 1, StartResources = new ResourceSet(10_000, 10_000, 10_000) };

        var (_, steps) = Simulator(setup).Trace([Gene.RoboticsFactory, Gene.RoboticsFactory, Gene.MetalMine]);

        Assert.Equal([Gene.RoboticsFactory, Gene.MetalMine], steps.Select(s => s.Gene));
    }
}
