using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Schemas;

namespace OgameSkaner.Core.Storage;

/// <summary>Which schema a planet follows and whether the runner works on it.</summary>
public sealed class SchemaAssignment
{
    public string PlanetId { get; set; } = string.Empty;

    public Guid? SchemaId { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>The Schemas page of one universe as the user left it.</summary>
public sealed class SchemaRunSettings
{
    public List<SchemaAssignment> Planets { get; set; } = [];

    /// <summary>Pause between two checks of all planets.</summary>
    public int CheckIntervalMinutes { get; set; } = 2;

    public Guid? SelectedSchemaId { get; set; }
}

public interface ISchemaStore
{
    /// <summary>The schema library (shared by all universes); a starter schema when nothing was saved yet.</summary>
    IReadOnlyList<BuildSchema> LoadLibrary();

    void SaveLibrary(IEnumerable<BuildSchema> schemas);

    SchemaRunSettings LoadSettings(string universe);

    void SaveSettings(string universe, SchemaRunSettings settings);
}

/// <summary>Library in <c>schemas.json</c>, assignments in <c>universes/{name}/schema-assignments.json</c>.</summary>
public sealed class JsonSchemaStore(IAppPaths paths) : ISchemaStore
{
    private readonly object _sync = new();

    private string LibraryFile => Path.Combine(paths.Root, "schemas.json");

    public IReadOnlyList<BuildSchema> LoadLibrary()
    {
        lock (_sync)
            return JsonFile.Read<List<BuildSchema>>(LibraryFile) ?? [CreateStarter()];
    }

    public void SaveLibrary(IEnumerable<BuildSchema> schemas)
    {
        lock (_sync)
            JsonFile.Write(LibraryFile, schemas.ToList());
    }

    public SchemaRunSettings LoadSettings(string universe)
    {
        lock (_sync)
            return JsonFile.Read<SchemaRunSettings>(SettingsFile(universe)) ?? new SchemaRunSettings();
    }

    public void SaveSettings(string universe, SchemaRunSettings settings)
    {
        lock (_sync)
            JsonFile.Write(SettingsFile(universe), settings);
    }

    private string SettingsFile(string universe) => Path.Combine(paths.UniverseDirectory(universe), "schema-assignments.json");

    /// <summary>A simple mine-first order to start from; the solar plant and storages come from the rules.</summary>
    public static BuildSchema CreateStarter()
    {
        var schema = new BuildSchema { Name = "Starter: mines" };
        BuildingType[] order =
        [
            BuildingType.MetalMine, BuildingType.MetalMine, BuildingType.MetalMine, BuildingType.CrystalMine,
            BuildingType.MetalMine, BuildingType.CrystalMine, BuildingType.DeuteriumSynthesizer, BuildingType.MetalMine,
            BuildingType.CrystalMine, BuildingType.MetalMine, BuildingType.DeuteriumSynthesizer, BuildingType.CrystalMine,
            BuildingType.MetalMine, BuildingType.CrystalMine, BuildingType.DeuteriumSynthesizer, BuildingType.MetalMine,
            BuildingType.CrystalMine, BuildingType.MetalMine, BuildingType.DeuteriumSynthesizer, BuildingType.CrystalMine,
            BuildingType.MetalMine
        ];
        foreach (var building in order)
            schema.AppendNextLevel(building);
        return schema;
    }
}
