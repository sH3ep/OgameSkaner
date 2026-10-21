using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Services;

/// <summary>
/// The fleets of several own planets with as few requests and planet switches as possible: every planet's ships and
/// stock come from the empire page (one request, no switch), and only as many fleet pages are read as it takes for what
/// the empire page lacks: the fleet slots (on every fleet page) and the cargo capacity of each wanted ship type (shown
/// only on a page of a planet that has that type). The preferred planet (the queue's home planet, usually the current
/// one) is read first when it helps. Planets missing from the empire page, or every planet when the empire page can't
/// be read, get their own fleet page as before.
/// </summary>
public static class EmpireFleets
{
    /// <param name="shipTypes">The types whose cargo capacity the caller needs (e.g. the cargo ships and probes).</param>
    /// <returns>One fleet per planet in <paramref name="planets"/>' order; the planets read get their own page.</returns>
    public static async Task<IReadOnlyList<(OwnPlanet Planet, FleetSnapshot Fleet)>> ReadAsync(
        IOgamexClient client, IReadOnlyList<OwnPlanet> planets, IReadOnlyCollection<string> shipTypes, PlanetId? preferred,
        CancellationToken cancellationToken = default)
    {
        if (planets.Count == 0)
            return [];

        EmpirePage? empire;
        try
        {
            empire = await client.GetEmpireAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OgamexException ex) when (ex is not SessionExpiredException)
        {
            empire = null; // markup changed? the fleet pages still work
        }

        var pages = new Dictionary<PlanetId, FleetSnapshot>();
        foreach (var planet in planets.Where(p => empire?.PlanetAt(p.Coordinates) is null))
            pages[planet.Id] = await client.GetFleetAsync(planet.Id, cancellationToken).ConfigureAwait(false);

        long Count(OwnPlanet planet, string type) =>
            pages.TryGetValue(planet.Id, out var page) ? page.QuantityOf(type) : empire!.PlanetAt(planet.Coordinates)!.ShipCount(type);

        // Each page read covers the capacities of the types its planet has: read the planet that covers the most
        // missing ones, the preferred one among equals.
        var byPreference = planets.OrderBy(p => p.Id == preferred ? 0 : 1).ToList();
        while (true)
        {
            var missing = shipTypes.Distinct()
                .Where(t => pages.Values.All(p => p.CargoCapacityOf(t) <= 0) && planets.Any(p => Count(p, t) > 0))
                .ToList();
            var candidates = byPreference
                .Where(p => !pages.ContainsKey(p.Id))
                .Select(p => (Planet: p, Covers: missing.Count(t => Count(p, t) > 0)))
                .Where(c => c.Covers > 0)
                .ToList();
            if (candidates.Count == 0)
                break; // a page without a type's capacity doesn't loop: its planet is read once
            var source = candidates.MaxBy(c => c.Covers).Planet; // the first of equals
            pages[source.Id] = await client.GetFleetAsync(source.Id, cancellationToken).ConfigureAwait(false);
        }

        // The slots are on every fleet page.
        if (pages.Count == 0)
            pages[byPreference[0].Id] = await client.GetFleetAsync(byPreference[0].Id, cancellationToken).ConfigureAwait(false);

        var template = pages.Values.FirstOrDefault(p => p.Slots is not null) ?? pages.Values.First();
        var capacities = pages.Values
            .SelectMany(p => p.CargoCapacities ?? new Dictionary<string, long>())
            .Where(c => c.Value > 0)
            .GroupBy(c => c.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        return planets
            .Select(planet => (planet, pages.TryGetValue(planet.Id, out var page)
                ? page with { CargoCapacities = capacities }
                : FromEmpire(empire!.PlanetAt(planet.Coordinates)!, planet.Coordinates, template, capacities)))
            .ToList();
    }

    /// <summary>A fleet page as it would look for <paramref name="planet"/>: its flying ships and stock, the slots and capacities read elsewhere.</summary>
    private static FleetSnapshot FromEmpire(EmpirePlanet planet, Coordinates coordinates, FleetSnapshot template, IReadOnlyDictionary<string, long> capacities) =>
        new(planet.Ships.Where(s => s.Value > 0 && UnitCatalog.CanFly(s.Key)).Select(s => new ShipStack(s.Key, s.Value)).ToList(),
            coordinates,
            template.Slots,
            capacities,
            template.ServerClock,
            planet.Resources,
            template.ExpeditionSlots);
}
