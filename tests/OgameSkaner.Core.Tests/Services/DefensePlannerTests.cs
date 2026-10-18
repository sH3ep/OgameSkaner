using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Services.Defense;

namespace OgameSkaner.Core.Tests.Services;

public class DefensePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly UniverseProfile Universe = new("tron");

    private static readonly OwnPlanet Home = new(new PlanetId("1"), "Home", new Coordinates(8, 260, 3));
    private static readonly OwnPlanet Near = new(new PlanetId("2"), "Near", new Coordinates(8, 262, 5));
    private static readonly OwnPlanet Far = new(new PlanetId("3"), "Far", new Coordinates(2, 10, 1));

    private static FleetMovement Hostile(string id, MovementMission mission, TimeSpan inTime, OwnPlanet? target = null, string? targetName = null) =>
        new(id, mission, MovementRelation.Hostile, false, Now + inTime, new Coordinates(8, 250, 7), "Raider",
            (target ?? Home).Coordinates, targetName ?? (target ?? Home).Name, 100, "Evil");

    [Fact]
    public void An_attack_is_due_its_lead_time_before_it_lands_and_only_once()
    {
        var settings = DefenseSettings.Default with { EscapeSecondsBeforeArrival = 60 };
        var attack = Hostile("a", MovementMission.Attack, TimeSpan.FromMinutes(5));

        Assert.Empty(DefensePlanner.DueEscapes([attack], new HashSet<string>(), Now, settings));
        Assert.Single(DefensePlanner.DueEscapes([attack], new HashSet<string>(), Now.AddMinutes(4), settings));
        Assert.Empty(DefensePlanner.DueEscapes([attack], new HashSet<string> { "a" }, Now.AddMinutes(4), settings));
        Assert.Empty(DefensePlanner.DueEscapes([attack], new HashSet<string>(), Now.AddMinutes(4), settings with { EscapeMode = FleetEscapeMode.Off }));
    }

    [Fact]
    public void Spies_and_missiles_are_never_escaped()
    {
        var movements = new[] { Hostile("s", MovementMission.Espionage, TimeSpan.Zero), Hostile("m", MovementMission.MissileAttack, TimeSpan.Zero) };

        Assert.Empty(DefensePlanner.DueEscapes(movements, new HashSet<string>(), Now, DefenseSettings.Default));
    }

    [Fact]
    public void The_wait_ends_when_the_next_escape_falls_due()
    {
        var settings = DefenseSettings.Default with { CheckIntervalSeconds = 30, EscapeSecondsBeforeArrival = 60 };

        Assert.Equal(TimeSpan.FromSeconds(30), DefensePlanner.NextWait([], new HashSet<string>(), Now, settings));
        Assert.Equal(TimeSpan.FromSeconds(10),
            DefensePlanner.NextWait([Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(70))], new HashSet<string>(), Now, settings));
        Assert.Equal(TimeSpan.FromSeconds(1),
            DefensePlanner.NextWait([Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(20))], new HashSet<string>(), Now, settings));
        Assert.Equal(TimeSpan.FromSeconds(30),
            DefensePlanner.NextWait([Hostile("a", MovementMission.Attack, TimeSpan.FromSeconds(20))], new HashSet<string> { "a" }, Now, settings));
    }

    [Fact]
    public void Spies_are_notified_only_when_wanted()
    {
        var spy = Hostile("s", MovementMission.Espionage, TimeSpan.Zero);

        Assert.True(DefensePlanner.ShouldNotify(spy, DefenseSettings.Default));
        Assert.False(DefensePlanner.ShouldNotify(spy, DefenseSettings.Default with { NotifyEspionage = false }));
        Assert.True(DefensePlanner.ShouldNotify(Hostile("m", MovementMission.MissileAttack, TimeSpan.Zero), DefenseSettings.Default with { NotifyEspionage = false }));
        Assert.False(DefensePlanner.ShouldNotify(spy with { Relation = MovementRelation.Own }, DefenseSettings.Default));
        Assert.False(DefensePlanner.ShouldNotify(spy with { Returning = true }, DefenseSettings.Default));
    }

    [Fact]
    public void A_target_named_unlike_the_planet_is_its_moon()
    {
        Assert.Equal(new ThreatTarget(Home, false), DefensePlanner.TargetOf(Hostile("a", MovementMission.Attack, TimeSpan.Zero), [Home, Near]));
        Assert.Equal(new ThreatTarget(Home, true), DefensePlanner.TargetOf(Hostile("a", MovementMission.Attack, TimeSpan.Zero, targetName: "Moon"), [Home, Near]));
        Assert.Equal(new ThreatTarget(null, false), DefensePlanner.TargetOf(Hostile("a", MovementMission.Attack, TimeSpan.Zero, Far), [Home, Near]));
    }

    [Fact]
    public void Destinations_are_the_moon_then_the_nearest_planets_not_under_attack()
    {
        var planets = new[] { Home, Far, Near };

        Assert.Equal(
            [new EscapeDestination(Home.Coordinates, PlanetType.Moon, "its moon"), new EscapeDestination(Near.Coordinates, PlanetType.Planet, "Near [8:262:5]"),
             new EscapeDestination(Far.Coordinates, PlanetType.Planet, "Far [2:10:1]")],
            DefensePlanner.Destinations(Home, planets, [Home.Coordinates], DefenseSettings.Default, Universe));
        Assert.Equal([PlanetType.Moon],
            DefensePlanner.Destinations(Home, planets, [], DefenseSettings.Default with { FallbackToNearestPlanet = false }, Universe).Select(d => d.TargetType));
        Assert.Equal([Far.Coordinates],
            DefensePlanner.Destinations(Home, planets, [Near.Coordinates], DefenseSettings.Default with { EscapeMode = FleetEscapeMode.NearestPlanet }, Universe)
                .Select(d => d.Target));
    }

    [Fact]
    public void Cargo_takes_deuterium_then_crystal_then_metal_and_leaves_the_fuel()
    {
        var available = new ResourceSet(1_000, 500, 300);

        Assert.Equal(new ResourceSet(0, 400, 200), DefensePlanner.Cargo(available, 600, fuel: 100));
        Assert.Equal(new ResourceSet(1_000, 500, 200), DefensePlanner.Cargo(available, 10_000, fuel: 100));
        Assert.Equal(new ResourceSet(50, 500, 0), DefensePlanner.Cargo(available, 550, fuel: 500));
    }

    [Fact]
    public void Satellites_and_crawlers_stay_home()
    {
        var fleet = new FleetSnapshot(
            [new ShipStack("SOLAR_SATELLITE", 100), new ShipStack("LIGHT_CARGO", 10), new ShipStack("CRAWLER", 5), new ShipStack("CRUISER", 2)],
            Home.Coordinates, CargoCapacities: new Dictionary<string, long> { ["LIGHT_CARGO"] = 5_000, ["CRUISER"] = 800 });

        var ships = DefensePlanner.EscapingShips(fleet);

        Assert.Equal(["LIGHT_CARGO", "CRUISER"], ships.Select(s => s.ShipType));
        Assert.Equal(51_600, DefensePlanner.CargoCapacity(ships, fleet));
    }
}
