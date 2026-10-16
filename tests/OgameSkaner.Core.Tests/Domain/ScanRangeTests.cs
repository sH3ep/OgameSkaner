using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Tests.Domain;

public class ScanRangeTests
{
    private static readonly UniverseProfile Universe = new("tron") { Galaxies = 9, Systems = 499 };

    [Fact]
    public void Count_and_enumeration_cover_the_system_range_in_every_galaxy()
    {
        var range = new ScanRange(1, 10, 2, 12);

        Assert.Equal(6, range.Count);
        Assert.Equal(
        [
            new SolarSystemAddress(1, 10), new SolarSystemAddress(1, 11), new SolarSystemAddress(1, 12),
            new SolarSystemAddress(2, 10), new SolarSystemAddress(2, 11), new SolarSystemAddress(2, 12)
        ], range.Enumerate());
    }

    [Fact]
    public void Validate_accepts_full_universe()
    {
        Assert.Empty(new ScanRange(1, 1, 9, 499).Validate(Universe));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 1, 10, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 1, 500)]
    [InlineData(2, 1, 1, 1)]
    [InlineData(1, 5, 1, 4)]
    public void Validate_rejects_invalid_ranges(int startGalaxy, int startSystem, int endGalaxy, int endSystem)
    {
        Assert.NotEmpty(new ScanRange(startGalaxy, startSystem, endGalaxy, endSystem).Validate(Universe));
    }

    [Fact]
    public void Validate_uses_universe_limits()
    {
        var small = new UniverseProfile("mini") { Galaxies = 2, Systems = 100 };

        Assert.NotEmpty(new ScanRange(1, 1, 3, 1).Validate(small));
        Assert.NotEmpty(new ScanRange(1, 1, 1, 101).Validate(small));
    }

    [Theory]
    [InlineData("tron", true)]
    [InlineData("uni-2", true)]
    [InlineData("Tron", false)]
    [InlineData("evil.com/x", false)]
    [InlineData("", false)]
    public void Universe_name_must_be_a_safe_host_label(string name, bool valid)
    {
        Assert.Equal(valid, UniverseProfile.IsValidName(name));
    }
}
