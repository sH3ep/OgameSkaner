using System.Runtime.CompilerServices;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;

namespace OgameSkaner.Core.Services.Scheduling;

/// <summary>One module's open claim as the Queue page shows it.</summary>
/// <param name="Count">The slots other modules count as used now.</param>
/// <param name="Reserved">For a standing reservation (<see cref="IFleetSlotClaims.Reserve"/>): all its slots, those in flight included; null for a claim.</param>
/// <param name="InFlight">For a reservation: its fleets in the air now (they hold their own slots).</param>
/// <param name="NextBackAt">For a reservation: when its next fleet frees its slot; null when none is in the air.</param>
public sealed record FleetSlotClaim(
    string Owner, int Count, DateTimeOffset Since, DateTimeOffset ExpiresAt, int? Reserved = null, int InFlight = 0, DateTimeOffset? NextBackAt = null)
{
    public bool IsReservation => Reserved is not null;
}

/// <summary>The fleet slots as a module last read them from the game (the fleet page), for the Queue page.</summary>
public sealed record FleetSlotsReading(FleetSlots Slots, string Owner, DateTimeOffset At);

/// <summary>
/// Fleet slots a module waits for. The action queue already keeps two modules from sending at the same moment, but not
/// from starving each other: whoever checks first fills the free slots. A module that needs slots claims them; every
/// other module then counts them as used (<see cref="Effective"/>), so fleets coming back free slots for the claimer,
/// which takes them on its next check. Claims go first come, first served, and expire unless renewed, so a stopped
/// module never blocks the others. The attack protection's escapes ignore claims.
/// <para>A module that always flies the same number of fleets (Expeditions) makes a standing reservation instead
/// (<see cref="Reserve"/>): its slots not in flight count as used for everyone else, ahead of every claim, from the moment
/// each of its fleets is due back, so no other module takes a returning fleet's slot. It lasts until released.</para>
/// </summary>
public interface IFleetSlotClaims
{
    /// <summary>Sets <paramref name="owner"/>'s claim to <paramref name="count"/> slots (0 releases it) and renews it for <paramref name="lifetime"/> (default <see cref="FleetSlotClaims.DefaultLifetime"/>); it keeps its place.</summary>
    void Claim(string owner, int count, TimeSpan? lifetime = null);

    /// <summary><paramref name="owner"/> sent <paramref name="fleets"/> fleets: its claim shrinks by that many.</summary>
    void Used(string owner, int fleets);

    /// <summary>
    /// Reserves <paramref name="slots"/> fleet slots for <paramref name="owner"/> until released (0 releases it). Its
    /// fleets in the air hold their slots themselves until <paramref name="busyUntil"/> (one time per fleet: when its slot
    /// frees); the rest counts as used for every other module.
    /// </summary>
    void Reserve(string owner, int slots, IReadOnlyCollection<DateTimeOffset> busyUntil);

    /// <summary>Ends <paramref name="owner"/>'s claim and reservation.</summary>
    void Release(string owner);

    /// <summary>
    /// The slots as <paramref name="owner"/> may use them: <paramref name="read"/> with the claims ahead of it counted
    /// as used (every claim of another module when it has none, the earlier ones when it has one; none for an owner with
    /// a reservation, which comes first) and the other modules' reservations too.
    /// </summary>
    FleetSlots Effective(string owner, FleetSlots read);

    /// <summary>The standing reservations, then the open claims oldest first.</summary>
    IReadOnlyList<FleetSlotClaim> Snapshot();

    /// <summary>The slots the last <see cref="Effective"/> call was given (as read from the game); null before the first.</summary>
    FleetSlotsReading? LastReading { get; }

    /// <summary>Raised when a claim is made, changed or released.</summary>
    event EventHandler? Changed;
}

/// <summary>One <see cref="IFleetSlotClaims"/> per client, like <see cref="IActionQueues"/>.</summary>
public interface IFleetSlotClaimsProvider
{
    IFleetSlotClaims For(IOgamexClient client);
}

public sealed class FleetSlotClaimsProvider(TimeProvider timeProvider) : IFleetSlotClaimsProvider
{
    private readonly ConditionalWeakTable<IOgamexClient, FleetSlotClaims> _claims = new();

    public IFleetSlotClaims For(IOgamexClient client) => _claims.GetValue(client, _ => new FleetSlotClaims(timeProvider));
}

public sealed class FleetSlotClaims(TimeProvider timeProvider) : IFleetSlotClaims
{
    /// <summary>The lifetime of a claim that isn't renewed.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly List<FleetSlotClaim> _claims = [];
    private readonly List<Reservation> _reservations = [];
    private FleetSlotsReading? _lastReading;

    public event EventHandler? Changed;

    public void Claim(string owner, int count, TimeSpan? lifetime = null)
    {
        if (count <= 0)
        {
            Release(owner);
            return;
        }

        var now = timeProvider.GetUtcNow();
        var expires = now + (lifetime ?? DefaultLifetime);
        lock (_sync)
        {
            Prune(now);
            var index = _claims.FindIndex(c => c.Owner == owner);
            if (index >= 0)
                _claims[index] = _claims[index] with { Count = count, ExpiresAt = expires };
            else
                _claims.Add(new FleetSlotClaim(owner, count, now, expires));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Used(string owner, int fleets)
    {
        if (fleets <= 0)
            return;
        lock (_sync)
        {
            var index = _claims.FindIndex(c => c.Owner == owner);
            if (index < 0)
                return;
            var left = _claims[index].Count - fleets;
            if (left > 0)
                _claims[index] = _claims[index] with { Count = left };
            else
                _claims.RemoveAt(index);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reserve(string owner, int slots, IReadOnlyCollection<DateTimeOffset> busyUntil)
    {
        if (slots <= 0)
        {
            bool removed;
            lock (_sync)
                removed = _reservations.RemoveAll(r => r.Owner == owner) > 0;
            if (removed)
                Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        lock (_sync)
        {
            var index = _reservations.FindIndex(r => r.Owner == owner);
            var since = index >= 0 ? _reservations[index].Since : timeProvider.GetUtcNow();
            var reservation = new Reservation(owner, slots, busyUntil.ToArray(), since);
            if (index >= 0)
                _reservations[index] = reservation;
            else
                _reservations.Add(reservation);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Release(string owner)
    {
        bool removed;
        lock (_sync)
            removed = (_claims.RemoveAll(c => c.Owner == owner) + _reservations.RemoveAll(r => r.Owner == owner)) > 0;
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public FleetSlotsReading? LastReading
    {
        get
        {
            lock (_sync)
                return _lastReading;
        }
    }

    public FleetSlots Effective(string owner, FleetSlots read)
    {
        int ahead;
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            _lastReading = new FleetSlotsReading(read, owner, now);
            Prune(now);
            // A reservation comes before every claim: its owner counts only the other reservations, so a claim never
            // takes the slots it reserved (it sends no more than it reserved anyway).
            var own = _claims.FindIndex(c => c.Owner == owner);
            var claimsAhead = _reservations.Any(r => r.Owner == owner) ? 0 : (own < 0 ? _claims : _claims.Take(own)).Sum(c => c.Count);
            ahead = claimsAhead + _reservations.Where(r => r.Owner != owner).Sum(r => r.Held(now));
        }

        return ahead == 0 ? read : read with { Used = Math.Min(read.Max, read.Used + ahead) };
    }

    public IReadOnlyList<FleetSlotClaim> Snapshot()
    {
        lock (_sync)
        {
            var now = timeProvider.GetUtcNow();
            Prune(now);
            return _reservations
                .Select(r => new FleetSlotClaim(r.Owner, r.Held(now), r.Since, DateTimeOffset.MaxValue, r.Slots, r.InFlight(now), r.NextBack(now)))
                .Concat(_claims)
                .ToList();
        }
    }

    private void Prune(DateTimeOffset now) => _claims.RemoveAll(c => c.ExpiresAt <= now);

    private sealed record Reservation(string Owner, int Slots, DateTimeOffset[] BusyUntil, DateTimeOffset Since)
    {
        /// <summary>The reserved slots no fleet of the owner holds at <paramref name="now"/>.</summary>
        public int Held(DateTimeOffset now) => Math.Max(0, Slots - InFlight(now));

        public int InFlight(DateTimeOffset now) => BusyUntil.Count(t => t > now);

        public DateTimeOffset? NextBack(DateTimeOffset now) => BusyUntil.Where(t => t > now).Select(t => (DateTimeOffset?)t).Min();
    }
}
