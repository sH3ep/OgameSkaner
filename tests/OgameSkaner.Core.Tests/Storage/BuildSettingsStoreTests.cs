using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class BuildSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Returns_defaults_with_one_rule_per_building_except_the_automatic_ones()
    {
        var settings = new JsonBuildSettingsStore(new AppPaths(_temp.Path)).Load("tron");

        Assert.Equal(10, settings.ReferenceLevel);
        Assert.Equal(BuildingCatalog.All.Select(d => d.Type).Where(t => !BuildStrategy.IsAutomatic(t)), settings.Rules.Select(r => r.Building));
        Assert.Equal(70, settings.StorageFillPercent);
        Assert.Equal(new BuildingRule(BuildingType.MetalMine, true, RuleMode.Relative, 2), settings.Rules[0]);
    }

    [Fact]
    public void Round_trips_per_universe()
    {
        var store = new JsonBuildSettingsStore(new AppPaths(_temp.Path));
        var settings = BuildSettings.CreateDefault();
        settings.ReferenceLevel = 17;
        settings.Mode = BuildAutomationMode.Auto;
        settings.ExcludedPlanetIds = ["123", "456"];
        settings.Rules[1] = settings.Rules[1] with { Mode = RuleMode.Absolute, Value = 9 };

        store.Save("tron", settings);
        var loaded = new JsonBuildSettingsStore(new AppPaths(_temp.Path)).Load("tron");

        Assert.Equal(17, loaded.ReferenceLevel);
        Assert.Equal(BuildAutomationMode.Auto, loaded.Mode);
        Assert.Equal(["123", "456"], loaded.ExcludedPlanetIds);
        Assert.Equal(settings.Rules, loaded.Rules);
        Assert.Equal(10, store.Load("other").ReferenceLevel);
    }

    [Fact]
    public void Normalize_adds_missing_buildings_and_drops_duplicates_and_automatic_ones()
    {
        var settings = new BuildSettings
        {
            Rules =
            [
                new(BuildingType.Shipyard, true, RuleMode.Absolute, 4),
                new(BuildingType.Shipyard, true, RuleMode.Absolute, 7),
                new(BuildingType.SolarPlant, true, RuleMode.Absolute, 7),
                new(BuildingType.MetalStorage, true, RuleMode.Absolute, 7)
            ]
        }.Normalize();

        Assert.Equal(BuildingCatalog.All.Count(d => !BuildStrategy.IsAutomatic(d.Type)), settings.Rules.Count);
        Assert.Equal(4, settings.Rules.Single(r => r.Building == BuildingType.Shipyard).Value);
        Assert.DoesNotContain(settings.Rules, r => BuildStrategy.IsAutomatic(r.Building));
    }
}
