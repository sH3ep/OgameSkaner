using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Storage;

/// <summary>The player's own planets in a universe, as last read from the server.</summary>
public sealed record OwnPlanetList(IReadOnlyList<OwnPlanet> Planets, DateTimeOffset? RefreshedAt)
{
    public static OwnPlanetList Empty { get; } = new([], null);
}

public interface IOwnPlanetStore
{
    /// <summary>Returns the stored planets, or an empty list when they were never refreshed.</summary>
    OwnPlanetList Load(string universe);

    void Save(string universe, OwnPlanetList planets);
}

/// <summary>Stores each universe's planets in <c>universes/{name}/own-planets.json</c>.</summary>
public sealed class JsonOwnPlanetStore(IAppPaths paths) : IOwnPlanetStore
{
    private readonly object _sync = new();

    public OwnPlanetList Load(string universe)
    {
        PlanetsDocument? document;
        lock (_sync)
            document = JsonFile.Read<PlanetsDocument>(FilePath(universe));

        return document is null
            ? OwnPlanetList.Empty
            : new OwnPlanetList(document.Planets.Select(r => r.ToDomain()).ToList(), document.RefreshedAt);
    }

    public void Save(string universe, OwnPlanetList planets)
    {
        var document = new PlanetsDocument
        {
            RefreshedAt = planets.RefreshedAt,
            Planets = planets.Planets.Select(PlanetRecord.FromDomain).ToList()
        };

        lock (_sync)
            JsonFile.Write(FilePath(universe), document);
    }

    private string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "own-planets.json");

    private sealed class PlanetsDocument
    {
        public DateTimeOffset? RefreshedAt { get; set; }
        public List<PlanetRecord> Planets { get; set; } = [];
    }

    /// <summary>Flat on-disk shape, decoupled from the domain model.</summary>
    private sealed record PlanetRecord(string Id, string Name, int Galaxy, int System, int Position)
    {
        public OwnPlanet ToDomain() => new(new PlanetId(Id), Name, new Coordinates(Galaxy, System, Position));

        public static PlanetRecord FromDomain(OwnPlanet p) =>
            new(p.Id.Value, p.Name, p.Coordinates.Galaxy, p.Coordinates.System, p.Coordinates.Position);
    }
}
