using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Services.Building;
using OgameSkaner.Core.Services.Transports;

namespace OgameSkaner.Core.Tests.Services;

public class ResourceSharePlannerTests
{
    private static readonly UniverseProfile Universe = new("genesis");
    private static readonly OwnPlanet Receiver = new(new PlanetId("r"), "Receiver", new Coordinates(1, 100, 5));
    private static readonly OwnPlanet Near = new(new PlanetId("n"), "Near", new Coordinates(1, 101, 5));
    private static readonly OwnPlanet Far = new(new PlanetId("f"), "Far", new Coordinates(3, 300, 5));

    private static readonly Dictionary<PlanetId, ResourceSet> NothingInFlight = new();

    private static SharePlanet Planet(OwnPlanet planet, long each, ShareDemand? demand = null, long? capacity = null, long production = 0) =>
        new(planet, new ResourceSet(each, each, each), new ResourceSet(production, production, production), capacity, demand);

    private static ShareDemand Wants(long each) => new(new ResourceSet(each, each, each), new ResourceSet(each, each, each));

    [Fact]
    public void A_waiting_planet_gets_what_it_misses_from_the_nearest_planet()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 100_000, Wants(500_000)), Planet(Far, 10_000_000), Planet(Near, 10_000_000)], NothingInFlight, 0, Universe);

        var transport = Assert.Single(plan);
        Assert.Equal((Near, Receiver, new ResourceSet(400_000, 400_000, 400_000)), (transport.From, transport.To, transport.Cargo));
    }

    [Fact]
    public void A_builder_planet_keeps_what_its_own_next_items_need()
    {
        var keeps = new ShareDemand(new ResourceSet(9_900_000, 9_900_000, 9_900_000), null);
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 100_000, Wants(500_000)), Planet(Near, 10_000_000, keeps), Planet(Far, 10_000_000)], NothingInFlight, 0, Universe);

        Assert.Equal([(Near, 100_000L), (Far, 300_000L)], plan.Select(t => (t.From, t.Cargo.Metal)));
    }

    [Fact]
    public void Cargo_on_its_way_counts_and_small_transports_are_dropped()
    {
        var inFlight = new Dictionary<PlanetId, ResourceSet> { [Receiver.Id] = new(390_000, 390_000, 390_000) };

        Assert.Empty(ResourceSharePlanner.Plan([Planet(Receiver, 100_000, Wants(500_000)), Planet(Near, 10_000_000)], inFlight, 50_000, Universe));
        Assert.Single(ResourceSharePlanner.Plan([Planet(Receiver, 100_000, Wants(500_000)), Planet(Near, 10_000_000)], inFlight, 30_000, Universe));
    }

    [Fact]
    public void The_cargo_ships_limit_a_donor_and_the_next_one_gives_the_rest()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000, capacity: 150_000), Planet(Far, 10_000_000)], NothingInFlight, 0, Universe);

        Assert.Equal([(Near, 150_000L), (Far, 150_000L)], plan.Select(t => (t.From, TotalOf(t.Cargo))));
    }

    [Fact]
    public void Donors_keep_their_deuterium_reserve_for_fuel()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(1_000_000)), Planet(Near, 1_200_000)], NothingInFlight, 0, Universe, deuteriumReserve: 500_000);

        Assert.Equal(new ResourceSet(1_000_000, 1_000_000, 700_000), Assert.Single(plan).Cargo);
    }

    [Fact]
    public void A_blocked_planet_neither_gives_nor_gets()
    {
        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)) with { Blocked = true }, Planet(Near, 10_000_000)], NothingInFlight, 0, Universe));
        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000) with { Blocked = true }], NothingInFlight, 0, Universe));
    }

    [Fact]
    public void The_receiver_production_tells_when_it_pays_the_rest_itself()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 100_000, Wants(500_000), production: 100_000), Planet(Near, 10_000_000)], NothingInFlight, 0, Universe);

        Assert.Equal(TimeSpan.FromHours(4), Assert.Single(plan).SelfFundedIn);
        Assert.Null(ResourceSharePlanner.SelfFundedIn(new ResourceSet(0, 1, 0), new ResourceSet(100, 0, 100)));
    }

    [Fact]
    public void Amounts_are_rounded_up_within_the_surplus_and_down_beyond_it()
    {
        // Missing 2,345,678 of each: the near donor rounds up to 2,400,000; with only 2,350,000 of surplus it rounds down.
        var plan = ResourceSharePlanner.Plan([Planet(Receiver, 154_322, Wants(2_500_000)), Planet(Near, 10_000_000)], NothingInFlight, 0, Universe);
        Assert.Equal(new ResourceSet(2_400_000, 2_400_000, 2_400_000), Assert.Single(plan).Cargo);

        plan = ResourceSharePlanner.Plan([Planet(Receiver, 154_322, Wants(2_500_000)), Planet(Near, 2_350_000)], NothingInFlight, 0, Universe);
        Assert.Equal(new ResourceSet(2_300_000, 2_300_000, 2_300_000), Assert.Single(plan).Cargo);
    }

    [Theory]
    [InlineData(2_345_678, 2_300_000, 2_400_000)]
    [InlineData(123_456, 120_000, 130_000)]
    [InlineData(99_500, 99_000, 100_000)]
    [InlineData(12_345, 12_000, 13_000)]
    [InlineData(800, 0, 1_000)]
    [InlineData(2_000_000, 2_000_000, 2_000_000)]
    public void Cargo_is_rounded_to_two_significant_figures_and_whole_thousands(long amount, long down, long up)
    {
        Assert.Equal((down, up), (CargoRounding.Down(amount), CargoRounding.Up(amount)));
    }

    private static long TotalOf(ResourceSet set) => set.Metal + set.Crystal + set.Deuterium;
}
