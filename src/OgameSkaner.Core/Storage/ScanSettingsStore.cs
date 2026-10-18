using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

public interface IScanSettingsStore
{
    /// <summary>Returns the saved settings of a universe, or defaults when none were saved.</summary>
    ScanSettings Load(string universe);

    void Save(string universe, ScanSettings settings);
}

/// <summary>Stores the settings in <c>universes/{name}/scan-settings.json</c>.</summary>
public sealed class JsonScanSettingsStore(IAppPaths paths) : IScanSettingsStore
{
    private readonly object _sync = new();

    public ScanSettings Load(string universe)
    {
        lock (_sync)
            return JsonFile.Read<ScanSettings>(FilePath(universe)) ?? ScanSettings.Default;
    }

    public void Save(string universe, ScanSettings settings)
    {
        lock (_sync)
            JsonFile.Write(FilePath(universe), settings);
    }

    private string FilePath(string universe) => Path.Combine(paths.UniverseDirectory(universe), "scan-settings.json");
}
