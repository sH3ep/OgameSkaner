namespace OgameSkaner.Core.Domain.Raiding;

public enum UnitKind
{
    Ship,
    Defense
}

/// <summary>A ship or defense unit with its server id and English name.</summary>
public sealed record UnitInfo(string Id, string Name, UnitKind Kind);

/// <summary>
/// The units of OgameX (ids from the shipyard and defense pages, see docs/ogamex-raid-api.md). Reports name their units
/// by an image named after the id (<c>/assets/images/V2/defense/MISSILE_LAUNCHER.gif</c>) and the English name.
/// </summary>
public static class UnitCatalog
{
    private static readonly UnitInfo[] All =
    [
        Ship("SOLAR_SATELLITE", "Solar Satellite"),
        Ship("CRAWLER", "Crawler"),
        Ship("SPY_PROBE", "Spy Probe"),
        Ship("LIGHT_CARGO", "Light Cargo"),
        Ship("HEAVY_CARGO", "Heavy Cargo"),
        Ship("RECYCLER", "Recycler"),
        Ship("COLONY_SHIP", "Colony Ship"),
        Ship("ASTEROID_MINER", "Asteroid Miner"),
        Ship("LIGHT_FIGHTER", "Light Fighter"),
        Ship("HEAVY_FIGHTER", "Heavy Fighter"),
        Ship("CRUISER", "Cruiser"),
        Ship("BATTLESHIP", "Battleship"),
        Ship("BATTLE_CRUISER", "Battle Cruiser"),
        Ship("PLANET_BOMBER", "Planet Bomber"),
        Ship("DESTROYER", "Destroyer"),
        Ship("REAPER", "Reaper"),
        Ship("GALLEON", "Galleon"),
        Ship("DEATH_STAR", "Death Star"),
        Ship("FALCON", "Falcon"),
        Ship("AVATAR", "Avatar"),

        Defense("MISSILE_LAUNCHER", "Missile Launcher"),
        Defense("LIGHT_LASER_TURRET", "Light Laser Turret"),
        Defense("HEAVY_LASER_TURRET", "Heavy Laser Turret"),
        Defense("ION_CANNON", "Ion Cannon"),
        Defense("GAUSS_CANNON", "Gauss Cannon"),
        Defense("PLASMA_CANNON", "Plasma Cannon"),
        Defense("DORA_GUN", "Dora Gun"),
        Defense("PHOTON_CANNON", "Photon Cannon"),
        Defense("SMALL_SHIELD_DOME", "Small Shield Dome"),
        Defense("LARGE_SHIELD_DOME", "Large Shield Dome"),
        Defense("ATMOSPHERIC_SHIELD", "Atmospheric Shield"),
        Defense("FORTRESS", "Fortress"),
        Defense("DOOM_CANNON", "Doom Cannon"),
        Defense("ORBITAL_DEFENSE_PLATFORM", "Orbital Defense Platform"),
        Defense("INTERCEPTOR", "Interceptor"),
        Defense("INTERPLANETARY_MISSILE", "Interplanetary Missile")
    ];

    private static readonly Dictionary<string, UnitInfo> ById = All.ToDictionary(u => u.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, UnitInfo> ByName = All.ToDictionary(u => Normalize(u.Name), StringComparer.Ordinal);

    public static IReadOnlyList<UnitInfo> Units => All;

    /// <summary>Cargo ships the Raids page offers.</summary>
    public static IReadOnlyList<UnitInfo> CargoShips { get; } = [ById["HEAVY_CARGO"], ById["LIGHT_CARGO"]];

    public static UnitInfo? Get(string id) => ById.GetValueOrDefault(id);

    /// <summary>Finds a unit by its id ("LIGHT_FIGHTER") or English name ("Light Fighter"), ignoring case and spacing.</summary>
    public static UnitInfo? Find(string idOrName)
    {
        if (ById.TryGetValue(idOrName.Trim().ToUpperInvariant(), out var byId))
            return byId;
        return ByName.GetValueOrDefault(Normalize(idOrName));
    }

    /// <summary>The English name for a catalog id, or the text itself.</summary>
    public static string NameOf(string unit) => Get(unit)?.Name ?? unit;

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static UnitInfo Ship(string id, string name) => new(id, name, UnitKind.Ship);

    private static UnitInfo Defense(string id, string name) => new(id, name, UnitKind.Defense);
}
