namespace OgameSkaner.Core.Storage;

/// <summary>Locations of the files the app reads and writes.</summary>
public interface IAppPaths
{
    string Root { get; }
    string ProfilesFile { get; }
    string SessionsFile { get; }
    string LogsDirectory { get; }

    /// <summary>Database backups and the data files saved before the move to SQLite.</summary>
    string BackupsDirectory { get; }

    string UniverseDirectory(string universe);
}

public sealed class AppPaths(string root) : IAppPaths
{
    /// <summary>%AppData%\OgameSkaner</summary>
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OgameSkaner"));

    public string Root { get; } = root;
    public string ProfilesFile => Path.Combine(Root, "profiles.json");
    public string SessionsFile => Path.Combine(Root, "sessions.json");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string BackupsDirectory => Path.Combine(Root, "backups");
    public string UniverseDirectory(string universe) => Path.Combine(Root, "universes", universe);
}
