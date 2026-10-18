using OgameSkaner.Core.Domain.Expeditions;

namespace OgameSkaner.Core.Storage;

/// <summary>The Expeditions page's options per universe.</summary>
public interface IExpeditionSettingsStore
{
    ExpeditionSettings Load(string universe);

    void Save(string universe, ExpeditionSettings settings);
}

/// <summary><c>universes/{name}/expedition-settings.json</c>.</summary>
public sealed class JsonExpeditionSettingsStore(IAppPaths paths) : IExpeditionSettingsStore
{
    private readonly object _sync = new();

    public ExpeditionSettings Load(string universe)
    {
        lock (_sync)
            return JsonFile.Read<ExpeditionSettings>(SettingsPath(universe)) ?? ExpeditionSettings.Default;
    }

    public void Save(string universe, ExpeditionSettings settings)
    {
        lock (_sync)
            JsonFile.Write(SettingsPath(universe), settings);
    }

    private string SettingsPath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "expedition-settings.json");
}
