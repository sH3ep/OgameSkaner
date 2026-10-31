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

        // Near's 100,000 of surplus doesn't cover it, so one transport from Far instead of two.
        Assert.Equal([(Far, 400_000L)], plan.Select(t => (t.From, t.Cargo.Metal)));
    }

    [Fact]
    public void One_transport_from_a_further_planet_beats_two_from_nearer_ones()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000, capacity: 150_000), Planet(Far, 10_000_000)], NothingInFlight, 0, Universe);

        Assert.Equal([(Far, 300_000L)], plan.Select(t => (t.From, TotalOf(t.Cargo))));
    }

    [Fact]
    public void While_a_transport_flies_to_a_planet_it_gets_no_other()
    {
        var inFlight = new Dictionary<PlanetId, ResourceSet> { [Receiver.Id] = new(100_000, 100_000, 100_000) };

        Assert.Empty(ResourceSharePlanner.Plan([Planet(Receiver, 100_000, Wants(500_000)), Planet(Near, 10_000_000)], inFlight, 0, Universe));
    }

    [Fact]
    public void Small_transports_are_dropped()
    {
        Assert.Empty(ResourceSharePlanner.Plan([Planet(Receiver, 490_000, Wants(500_000)), Planet(Near, 10_000_000)], NothingInFlight, 50_000, Universe));
        Assert.Single(ResourceSharePlanner.Plan([Planet(Receiver, 490_000, Wants(500_000)), Planet(Near, 10_000_000)], NothingInFlight, 30_000, Universe));
    }

    [Fact]
    public void When_no_donor_covers_it_alone_nothing_is_sent()
    {
        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000, capacity: 90_000), Planet(Far, 10_000_000, capacity: 210_000)],
            NothingInFlight, 0, Universe));
        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(1_000_000)), Planet(Near, 600_000), Planet(Far, 600_000)], NothingInFlight, 0, Universe));
    }

    [Fact]
    public void Donors_keep_their_deuterium_reserve_for_fuel()
    {
        // With 1,200,000 the reserve leaves 700,000 of deuterium: not all of it, so nothing goes.
        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(1_000_000)), Planet(Near, 1_200_000)], NothingInFlight, 0, Universe, deuteriumReserve: 500_000));

        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(1_000_000)), Planet(Near, 1_600_000)], NothingInFlight, 0, Universe, deuteriumReserve: 500_000);
        Assert.Equal(new ResourceSet(1_000_000, 1_000_000, 1_000_000), Assert.Single(plan).Cargo);
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
    public void The_receiver_production_limits_the_flight()
    {
        // It pays the 400,000 itself in 4 h: a flight must save more than its round trip, so at most 4 h / 3.
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 100_000, Wants(500_000), production: 100_000), Planet(Near, 10_000_000)], NothingInFlight, 0, Universe);

        Assert.Equal(TimeSpan.FromMinutes(80), Assert.Single(plan).MaxFlight);
        Assert.Null(ResourceSharePlanner.SelfFundedIn(new ResourceSet(0, 1, 0), new ResourceSet(100, 0, 100)));
    }

    [Theory]
    [InlineData(4, 0, 1, 80)]     // landing at once: s/3
    [InlineData(4, 0, 3.5, 30)]   // must save 3.5 h: s - 3.5 h
    [InlineData(4, 3, 1, 30)]     // the queue is busy 3 h: landing before then saves 1 h, more than a 30 min round trip
    [InlineData(10, 2, 1, 200)]   // landing after the busy queue still saves twice the flight
    public void A_flight_is_worth_it_while_it_saves_more_than_its_round_trip(double selfFundedHours, double neededInHours, double minHours, double maxMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(maxMinutes), ResourceSharePlanner.MaxFlight(
            TimeSpan.FromHours(selfFundedHours), TimeSpan.FromHours(neededInHours), TimeSpan.FromHours(minHours)));
    }

    [Fact]
    public void A_planet_gets_only_what_its_production_does_not_make_before_its_queue_is_free()
    {
        var now = DateTimeOffset.UtcNow;
        var receiver = Planet(Receiver, 100_000, Wants(500_000) with { NeededAt = now.AddHours(3) }, production: 100_000);

        var transport = Assert.Single(ResourceSharePlanner.Plan([receiver, Planet(Near, 10_000_000)], NothingInFlight, 0, Universe,
            now: now, minTimeSaved: TimeSpan.FromHours(1)));

        Assert.Equal(new ResourceSet(100_000, 100_000, 100_000), transport.Cargo);
        Assert.Equal(TimeSpan.FromMinutes(30), transport.MaxFlight);
    }

    [Theory]
    [InlineData(3.5)] // it pays the rest itself 30 min after its queue is free: less than the hour a transport must save
    [InlineData(5)]   // it has all before its queue is free
    public void No_transport_when_it_saves_too_little(double queueBusyHours)
    {
        var now = DateTimeOffset.UtcNow;
        var receiver = Planet(Receiver, 100_000, Wants(500_000) with { NeededAt = now.AddHours(queueBusyHours) }, production: 100_000);

        Assert.Empty(ResourceSharePlanner.Plan([receiver, Planet(Near, 10_000_000)], NothingInFlight, 0, Universe,
            now: now, minTimeSaved: TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Planets_that_wait_for_stock_or_just_got_some_give_nothing()
    {
        // Near waits for more itself, so its stock stays; Far gives.
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000, Wants(10_050_000)), Planet(Far, 10_000_000)], NothingInFlight, 0, Universe);
        Assert.DoesNotContain(plan, t => t.From == Near);
        Assert.Contains(plan, t => t.From == Far && t.To == Receiver);

        Assert.Empty(ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000) with { ReceivedRecently = true }], NothingInFlight, 0, Universe));
    }

    [Fact]
    public void A_skipped_donor_leaves_the_receiver_to_the_next()
    {
        var plan = ResourceSharePlanner.Plan(
            [Planet(Receiver, 0, Wants(100_000)), Planet(Near, 10_000_000), Planet(Far, 10_000_000)], NothingInFlight, 0, Universe,
            skip: new HashSet<(PlanetId, PlanetId)> { (Near.Id, Receiver.Id) });

        Assert.Equal(Far, Assert.Single(plan).From);
    }

    [Fact]
    public void Amounts_are_rounded_up_within_the_surplus_else_exact()
    {
        // Missing 2,345,678 of each: the near donor rounds up to 2,400,000; with only 2,350,000 of surplus it sends the exact amount.
        var plan = ResourceSharePlanner.Plan([Planet(Receiver, 154_322, Wants(2_500_000)), Planet(Near, 10_000_000)], NothingInFlight, 0, Universe);
        Assert.Equal(new ResourceSet(2_400_000, 2_400_000, 2_400_000), Assert.Single(plan).Cargo);

        plan = ResourceSharePlanner.Plan([Planet(Receiver, 154_322, Wants(2_500_000)), Planet(Near, 2_350_000)], NothingInFlight, 0, Universe);
        Assert.Equal(new ResourceSet(2_345_678, 2_345_678, 2_345_678), Assert.Single(plan).Cargo);
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
