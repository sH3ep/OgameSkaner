using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Services;

public class ColonizationSearchTests
{
    private static ColonizationFilter Filter(int galaxyFrom = 1, int galaxyTo = 9, int systemFrom = 1, int systemTo = 499, int positionFrom = 1, int positionTo = 15) =>
        new(new ScanRange(galaxyFrom, systemFrom, galaxyTo, systemTo), positionFrom, positionTo);

    private static ScannedSystem Scanned(int galaxy, int system) => new(new SolarSystemAddress(galaxy, system), ScanTime);

    [Fact]
    public void Free_slots_are_the_positions_without_planets()
    {
        var result = ColonizationSearch.Find(
            [Planet("A", 1, 1, 1), Planet("B", 1, 1, 3)],
            [Scanned(1, 1)],
            Filter(positionTo: 4));

        Assert.Equal(["1:1:2", "1:1:4"], result.Slots.Select(s => s.Coordinates.ToString()));
        Assert.All(result.Slots, s => Assert.Equal(2, s.PlanetsInSystem));
    }

    [Fact]
    public void An_empty_scanned_system_has_every_slot_free()
    {
        var result = ColonizationSearch.Find([], [Scanned(2, 5)], Filter());

        Assert.Equal(Enumerable.Range(1, 15), result.Slots.Select(s => s.Coordinates.Position));
        Assert.All(result.Slots, s => Assert.Equal(ScanTime, s.ScannedAt));
    }

    [Fact]
    public void Unscanned_systems_are_skipped_and_counted()
    {
        var result = ColonizationSearch.Find([], [Scanned(1, 2)], Filter(galaxyTo: 1, systemFrom: 1, systemTo: 3));

        Assert.All(result.Slots, s => Assert.Equal(new SolarSystemAddress(1, 2), s.Coordinates.SolarSystem));
        Assert.Equal(3, result.SystemsInRange);
        Assert.Equal(1, result.SystemsScanned);
    }

    [Fact]
    public void Galaxy_system_and_slot_ranges_are_honoured()
    {
        var result = ColonizationSearch.Find(
            [Planet("A", 2, 10, 8)],
            [Scanned(1, 10), Scanned(2, 9), Scanned(2, 10), Scanned(2, 11), Scanned(3, 10)],
            Filter(galaxyFrom: 2, galaxyTo: 2, systemFrom: 10, systemTo: 10, positionFrom: 7, positionTo: 9));

        Assert.Equal(["2:10:7", "2:10:9"], result.Slots.Select(s => s.Coordinates.ToString()));
    }

    [Fact]
    public void Results_are_ordered_by_coordinates()
    {
        var result = ColonizationSearch.Find([], [Scanned(2, 1), Scanned(1, 7), Scanned(1, 3)], Filter(positionTo: 1));

        Assert.Equal(["1:3:1", "1:7:1", "2:1:1"], result.Slots.Select(s => s.Coordinates.ToString()));
    }

    [Theory]
    [InlineData(1, 9, 1, 499, 0, 15)]
    [InlineData(1, 9, 1, 499, 1, 16)]
    [InlineData(1, 9, 1, 499, 9, 3)]
    [InlineData(1, 10, 1, 499, 1, 15)]
    [InlineData(3, 2, 1, 499, 1, 15)]
    public void Validate_rejects_bad_ranges(int galaxyFrom, int galaxyTo, int systemFrom, int systemTo, int positionFrom, int positionTo)
    {
        var filter = Filter(galaxyFrom, galaxyTo, systemFrom, systemTo, positionFrom, positionTo);

        Assert.NotEmpty(filter.Validate(new UniverseProfile("tron")));
    }

    [Fact]
    public void Validate_accepts_the_whole_universe()
    {
        Assert.Empty(Filter().Validate(new UniverseProfile("tron")));
    }
}
