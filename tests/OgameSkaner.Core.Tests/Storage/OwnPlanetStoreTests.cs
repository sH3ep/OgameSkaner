using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Tests.Fakes;

namespace OgameSkaner.Core.Tests.Storage;

public class OwnPlanetStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Is_empty_before_the_first_refresh() =>
        Assert.Equal(OwnPlanetList.Empty, new JsonOwnPlanetStore(new AppPaths(_temp.Path)).Load("tron"));

    [Fact]
    public void Round_trips_per_universe()
    {
        var list = new OwnPlanetList(
            [new OwnPlanet(new PlanetId("33"), "Home", new Coordinates(1, 2, 3)), new OwnPlanet(new PlanetId("34"), "Colony", new Coordinates(4, 5, 6))],
            new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(2)));

        new JsonOwnPlanetStore(new AppPaths(_temp.Path)).Save("tron", list);
        var loaded = new JsonOwnPlanetStore(new AppPaths(_temp.Path)).Load("tron");

        Assert.Equal(list.Planets, loaded.Planets);
        Assert.Equal(list.RefreshedAt, loaded.RefreshedAt);
        Assert.Empty(new JsonOwnPlanetStore(new AppPaths(_temp.Path)).Load("other").Planets);
    }
}
