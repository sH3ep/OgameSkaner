using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class SchemaStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private JsonSchemaStore Store() => new(new AppPaths(_temp.Path));

    [Fact]
    public void Starts_with_a_starter_schema()
    {
        var schema = Assert.Single(Store().LoadLibrary());

        Assert.Equal(new SchemaStep(BuildingType.MetalMine, 1), schema.Steps[0]);
        Assert.All(schema.Steps.GroupBy(s => s.Building), g => Assert.Equal(Enumerable.Range(1, g.Count()), g.Select(s => s.Level)));
    }

    [Fact]
    public void Library_round_trips()
    {
        var schema = new BuildSchema { Name = "Colony", Rules = { SolarMaxLevel = 12, UseSolarSatellites = false, NaniteMaxLevel = 2 } };
        schema.AppendNextLevel(BuildingType.SolarPlant);
        schema.AppendNextLevel(BuildingType.RoboticsFactory);

        Store().SaveLibrary([schema]);
        var loaded = Assert.Single(Store().LoadLibrary());

        Assert.Equal((schema.Id, "Colony"), (loaded.Id, loaded.Name));
        Assert.Equal(schema.Steps, loaded.Steps);
        Assert.Equal((12, false, 2), (loaded.Rules.SolarMaxLevel, loaded.Rules.UseSolarSatellites, loaded.Rules.NaniteMaxLevel));
    }

    [Fact]
    public void Settings_round_trip_per_universe()
    {
        var id = Guid.NewGuid();
        Store().SaveSettings("tron", new SchemaRunSettings
        {
            CheckIntervalMinutes = 7,
            SelectedSchemaId = id,
            Planets = [new SchemaAssignment { PlanetId = "p1", SchemaId = id, Enabled = false }]
        });

        var loaded = Store().LoadSettings("tron");

        Assert.Equal(7, loaded.CheckIntervalMinutes);
        Assert.Equal(id, loaded.SelectedSchemaId);
        Assert.Equal(("p1", (Guid?)id, false), (loaded.Planets[0].PlanetId, loaded.Planets[0].SchemaId, loaded.Planets[0].Enabled));
        Assert.Empty(Store().LoadSettings("other").Planets);
    }
}
