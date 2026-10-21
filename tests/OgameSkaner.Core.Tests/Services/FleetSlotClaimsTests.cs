using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Services.Scheduling;

namespace OgameSkaner.Core.Tests.Services;

public class FleetSlotClaimsTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly FleetSlotClaims _claims;

    public FleetSlotClaimsTests() => _claims = new FleetSlotClaims(_clock);

    [Fact]
    public void Without_claims_the_slots_are_as_read()
    {
        Assert.Equal(new FleetSlots(3, 10), _claims.Effective("Raids", new FleetSlots(3, 10)));
    }

    [Fact]
    public void Other_modules_count_a_claim_as_used_and_the_claimer_does_not()
    {
        _claims.Claim("Builder", 2);

        Assert.Equal(new FleetSlots(5, 10), _claims.Effective("Raids", new FleetSlots(3, 10)));
        Assert.Equal(new FleetSlots(3, 10), _claims.Effective("Builder", new FleetSlots(3, 10)));
        Assert.Equal(new FleetSlots(10, 10), _claims.Effective("Raids", new FleetSlots(9, 10)));
    }

    [Fact]
    public void Claims_go_first_come_first_served()
    {
        _claims.Claim("Builder", 2);
        _claims.Claim("Moons", 1);
        _claims.Claim("Builder", 1); // renewing keeps the place

        Assert.Equal(new FleetSlots(1, 10), _claims.Effective("Builder", new FleetSlots(1, 10)));
        Assert.Equal(new FleetSlots(2, 10), _claims.Effective("Moons", new FleetSlots(1, 10)));
        Assert.Equal(new FleetSlots(3, 10), _claims.Effective("Raids", new FleetSlots(1, 10)));
    }

    [Fact]
    public void Sending_shrinks_a_claim_and_release_ends_it()
    {
        _claims.Claim("Builder", 2);
        _claims.Used("Builder", 1);
        Assert.Equal(1, Assert.Single(_claims.Snapshot()).Count);

        _claims.Used("Builder", 1);
        Assert.Empty(_claims.Snapshot());

        _claims.Claim("Moons", 1);
        _claims.Release("Moons");
        Assert.Equal(new FleetSlots(3, 10), _claims.Effective("Raids", new FleetSlots(3, 10)));
    }

    [Fact]
    public void A_claim_that_is_not_renewed_expires()
    {
        _claims.Claim("Builder", 2, TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Single(_claims.Snapshot());

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(_claims.Snapshot());
        Assert.Equal(new FleetSlots(3, 10), _claims.Effective("Raids", new FleetSlots(3, 10)));
    }

    [Fact]
    public void A_reservation_holds_the_slots_its_fleets_free_from_the_moment_they_are_back()
    {
        var now = _clock.GetUtcNow();
        _claims.Claim("Builder", 1);
        _claims.Reserve("Expeditions", 5, [now.AddMinutes(10), now.AddMinutes(20), now.AddMinutes(30)]);

        // 3 expeditions in the air hold their own slots; the other 2 count as used for everyone else, before the claims.
        Assert.Equal(new FleetSlots(6, 20), _claims.Effective("Raids", new FleetSlots(3, 20)));
        Assert.Equal(new FleetSlots(5, 20), _claims.Effective("Builder", new FleetSlots(3, 20)));
        Assert.Equal(new FleetSlots(3, 20), _claims.Effective("Expeditions", new FleetSlots(3, 20))); // claims don't take its slots
        Assert.Equal((2, 5), (_claims.Snapshot()[0].Count, _claims.Snapshot()[0].Reserved!.Value));

        // One is back: its slot is held at once (3); the Builder's claim (10 min) has expired.
        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(new FleetSlots(5, 20), _claims.Effective("Raids", new FleetSlots(2, 20)));

        // All are back: the reservation holds all 5 and, unlike a claim, doesn't expire.
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(new FleetSlots(5, 20), _claims.Effective("Raids", new FleetSlots(0, 20)));

        _claims.Release("Expeditions");
        Assert.Equal(new FleetSlots(0, 20), _claims.Effective("Raids", new FleetSlots(0, 20)));
    }

    [Fact]
    public void The_snapshot_shows_a_reservations_fleets_and_the_last_reading()
    {
        var now = _clock.GetUtcNow();
        Assert.Null(_claims.LastReading);
        _claims.Reserve("Expeditions", 5, [now.AddMinutes(30), now.AddMinutes(10)]);
        _claims.Claim("Builder", 2);
        _claims.Effective("Raids", new FleetSlots(12, 20));

        var reservation = _claims.Snapshot()[0];
        Assert.Equal(("Expeditions", true, 3, 5, 2, now.AddMinutes(10)),
            (reservation.Owner, reservation.IsReservation, reservation.Count, reservation.Reserved!.Value, reservation.InFlight, reservation.NextBackAt!.Value));
        Assert.False(_claims.Snapshot()[1].IsReservation);
        Assert.Equal(new FleetSlotsReading(new FleetSlots(12, 20), "Raids", now), _claims.LastReading);

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal((4, 1, now.AddMinutes(30)), (_claims.Snapshot()[0].Count, _claims.Snapshot()[0].InFlight, _claims.Snapshot()[0].NextBackAt!.Value));
    }

    [Fact]
    public void Claiming_no_slots_releases()
    {
        _claims.Claim("Builder", 2);
        _claims.Claim("Builder", 0);
        Assert.Empty(_claims.Snapshot());
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
