using OgameSkaner.Core.Domain.Moons;

namespace OgameSkaner.Core.Storage;

/// <summary>The Builder's Moons tab options per universe.</summary>
public interface IMoonSettingsStore
{
    MoonSettings Load(string universe);

    void Save(string universe, MoonSettings settings);
}

/// <summary><c>universes/{name}/moon-settings.json</c>.</summary>
public sealed class JsonMoonSettingsStore(IAppPaths paths) : IMoonSettingsStore
{
    private readonly object _sync = new();

    public MoonSettings Load(string universe)
    {
        lock (_sync)
            return JsonFile.Read<MoonSettings>(SettingsPath(universe)) ?? MoonSettings.Default;
    }

    public void Save(string universe, MoonSettings settings)
    {
        lock (_sync)
            JsonFile.Write(SettingsPath(universe), settings);
    }

    private string SettingsPath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "moon-settings.json");
}
