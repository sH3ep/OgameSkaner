using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Services.Expeditions;

namespace OgameSkaner.Core.Tests.Services;

public class ExpeditionPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));

    private static FleetMovement Expedition(string id, string originName = "Home", bool returning = false, Coordinates? origin = null) =>
        new(id, MovementMission.Expedition, MovementRelation.Own, returning, Now.AddHours(1), origin ?? Home.Coordinates, originName,
            new Coordinates(8, 260, 16), "", 100);

    [Fact]
    public void Expeditions_in_flight_are_counted_once_per_fleet_and_origin()
    {
        FleetMovement[] movements =
        [
            Expedition("a"),
            Expedition("a", returning: true),
            Expedition("b"),
            Expedition("c", origin: new Coordinates(8, 260, 4)),
            Expedition("d") with { Relation = MovementRelation.Hostile },
            Expedition("e") with { Mission = MovementMission.Transport }
        ];

        Assert.Equal(2, ExpeditionPlanner.InFlight(movements, Home, sharedCoordinates: false));
    }

    [Fact]
    public void A_planet_and_its_moon_are_told_apart_by_name()
    {
        var moon = new OwnPlanet(new PlanetId("m"), "Luna", Home.Coordinates);
        FleetMovement[] movements = [Expedition("a"), Expedition("b", originName: "Luna"), Expedition("c", originName: "Luna")];

        Assert.Equal(1, ExpeditionPlanner.InFlight(movements, Home, sharedCoordinates: true));
        Assert.Equal(2, ExpeditionPlanner.InFlight(movements, moon, sharedCoordinates: true));
    }

    [Theory]
    [InlineData(2, 11, 0, 12, 1, 8)] // fleets 9 free - 1 kept
    [InlineData(2, 11, 10, 12, 1, 2)] // expedition slots
    [InlineData(10, 11, 0, 12, 1, 0)] // only the kept slot is free
    [InlineData(11, 11, 0, 12, 0, 0)]
    public void The_budget_is_the_smaller_of_free_expedition_and_fleet_slots(int fleetsUsed, int fleetsMax, int expUsed, int expMax, int keep, int expected) =>
        Assert.Equal(expected, ExpeditionPlanner.Budget(new FleetSlots(fleetsUsed, fleetsMax), new FleetSlots(expUsed, expMax), keep));

    [Fact]
    public void Without_expedition_slots_the_fleet_slots_decide() =>
        Assert.Equal(3, ExpeditionPlanner.Budget(new FleetSlots(7, 11), null, 1));

    [Fact]
    public void Each_expedition_takes_its_share_of_the_ticked_ships()
    {
        var fleet = new FleetSnapshot(
            [new ShipStack("LIGHT_CARGO", 10), new ShipStack("HEAVY_CARGO", 3), new ShipStack("SPY_PROBE", 7), new ShipStack("SOLAR_SATELLITE", 50)],
            Home.Coordinates);
        string[] types = ["LIGHT_CARGO", "HEAVY_CARGO", "SOLAR_SATELLITE", "DEATH_STAR"];

        Assert.Equal([new ShipStack("LIGHT_CARGO", 4), new ShipStack("HEAVY_CARGO", 1)], ExpeditionPlanner.Fleet(fleet, types, 3));
        Assert.Equal([new ShipStack("LIGHT_CARGO", 10), new ShipStack("HEAVY_CARGO", 3)], ExpeditionPlanner.Fleet(fleet, types, 1));
        Assert.Empty(ExpeditionPlanner.Fleet(fleet, ["DEATH_STAR"], 1));
    }

    [Fact]
    public void Three_expeditions_share_all_ships_with_nothing_left()
    {
        var home = 10L;
        for (var remaining = 3; remaining > 0; remaining--)
        {
            var ships = ExpeditionPlanner.Fleet(new FleetSnapshot([new ShipStack("LIGHT_CARGO", home)], Home.Coordinates), ["LIGHT_CARGO"], remaining);
            home -= Assert.Single(ships).Quantity;
        }

        Assert.Equal(0, home);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(100, 120)]
    [InlineData(90, 60)] // a tie takes the shorter one
    [InlineData(5000, 720)]
    public void The_duration_is_the_nearest_one_offered(int wanted, int expected) =>
        Assert.Equal(expected, ExpeditionPlanner.Hold([60, 120, 180, 720], wanted));

    [Fact]
    public void The_wanted_duration_is_kept_without_offers() => Assert.Equal(90, ExpeditionPlanner.Hold([], 90));

    [Fact]
    public void The_target_is_position_16_of_the_origins_system() =>
        Assert.Equal(new Coordinates(8, 260, 16), ExpeditionPlanner.Target(Home.Coordinates));

    [Fact]
    public void The_wait_ends_shortly_after_the_next_change_within_limits()
    {
        var interval = TimeSpan.FromMinutes(5);

        Assert.Equal(TimeSpan.FromSeconds(150), ExpeditionPlanner.NextWait([Now.AddMinutes(2), Now.AddHours(1)], Now, interval));
        Assert.Equal(interval, ExpeditionPlanner.NextWait([Now.AddHours(1)], Now, interval));
        Assert.Equal(TimeSpan.FromSeconds(30), ExpeditionPlanner.NextWait([Now.AddSeconds(-20)], Now, interval));
        Assert.Equal(interval, ExpeditionPlanner.NextWait([Now.AddMinutes(-5)], Now, interval));
    }
}
