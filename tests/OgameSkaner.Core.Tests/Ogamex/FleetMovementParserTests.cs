using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Defense;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class FleetMovementParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 49, 19, TimeSpan.Zero);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_reads_own_and_hostile_fleets()
    {
        var movements = FleetMovementParser.Parse(Fixture("fleet_movements.html"), Now);

        Assert.Equal(4, movements.Count);
        Assert.Equal(
            new FleetMovement("0b6c7a52-6a1f-4f43-9a51-6f1f0b2d1a01", MovementMission.Attack, MovementRelation.Own, Returning: true, Now.AddSeconds(15),
                new Coordinates(8, 260, 3), "Home planet", new Coordinates(8, 102, 10), "8-102", 2_178_533, ReturnTimestamp: 1792315276),
            movements[0]);
        Assert.Equal(1792315500, movements[1].ReturnTimestamp);
        Assert.Null(movements[2].ReturnTimestamp); // hostile: no return button
        Assert.Equal(MovementMission.Expedition, movements[1].Mission);
        Assert.False(movements[1].Returning);
        Assert.Equal(new Coordinates(8, 260, 16), movements[1].Target);
        Assert.Equal(string.Empty, movements[1].TargetName);
    }

    [Fact]
    public void Parse_reads_the_attacker_and_the_attacked_planet()
    {
        var attack = FleetMovementParser.Parse(Fixture("fleet_movements.html"), Now)[2];

        Assert.Equal(MovementRelation.Hostile, attack.Relation);
        Assert.True(attack.IsIncomingHostile);
        Assert.True(attack.IsAttack);
        Assert.Equal(Now.AddMinutes(5), attack.ArrivesAt);
        Assert.Equal(new Coordinates(8, 250, 7), attack.Origin);
        Assert.Equal("Raider base", attack.OriginName);
        Assert.Equal("Evil & Co", attack.SenderName);
        Assert.Equal(new Coordinates(8, 260, 3), attack.Target);
        Assert.Equal("Home planet", attack.TargetName);
        Assert.Equal(12_000, attack.ShipCount);
    }

    [Theory]
    [InlineData("ACS_ATTACK", MovementMission.AcsAttack)]
    [InlineData("MISSILE_ATTACK", MovementMission.MissileAttack)]
    [InlineData("ESPIONAGE", MovementMission.Espionage)]
    [InlineData("ASTEROID_MINING", MovementMission.AsteroidMining)]
    [InlineData("SOMETHING_NEW", MovementMission.Unknown)]
    [InlineData("5", MovementMission.Unknown)]
    public void Mission_ids_map_to_the_enum(string serverId, MovementMission expected) =>
        Assert.Equal(expected, FleetMovementParser.ParseMission(serverId));

    [Fact]
    public void An_empty_list_has_no_fleets_and_a_whole_page_means_the_session_expired()
    {
        Assert.Empty(FleetMovementParser.Parse("<table id=\"fleet-movement-table\" class=\"fleet-movement-table\"></table>", Now));
        Assert.Empty(FleetMovementParser.Parse(string.Empty, Now));
        Assert.Throws<SessionExpiredException>(() => FleetMovementParser.Parse("<!DOCTYPE html><html><body>Login</body></html>", Now));
    }
}
