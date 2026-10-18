using OgameSkaner.Core.Domain.Alerts;
using OgameSkaner.Core.Domain.Defense;

namespace OgameSkaner.Core.Storage;

/// <summary>The Defense page's options per universe and the alert channels' options shared by all universes.</summary>
public interface IDefenseSettingsStore
{
    DefenseSettings Load(string universe);

    void Save(string universe, DefenseSettings settings);

    AlertSettings LoadAlerts();

    void SaveAlerts(AlertSettings settings);
}

/// <summary><c>universes/{name}/defense-settings.json</c> and <c>alert-settings.json</c>.</summary>
public sealed class JsonDefenseSettingsStore(IAppPaths paths) : IDefenseSettingsStore
{
    private readonly object _sync = new();

    public DefenseSettings Load(string universe)
    {
        lock (_sync)
            return JsonFile.Read<DefenseSettings>(SettingsPath(universe)) ?? DefenseSettings.Default;
    }

    public void Save(string universe, DefenseSettings settings)
    {
        lock (_sync)
            JsonFile.Write(SettingsPath(universe), settings);
    }

    public AlertSettings LoadAlerts()
    {
        lock (_sync)
            return JsonFile.Read<AlertSettings>(AlertsPath) ?? AlertSettings.Default;
    }

    public void SaveAlerts(AlertSettings settings)
    {
        lock (_sync)
            JsonFile.Write(AlertsPath, settings);
    }

    private string SettingsPath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "defense-settings.json");

    private string AlertsPath => Path.Combine(paths.Root, "alert-settings.json");
}
