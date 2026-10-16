using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage;

/// <summary>The configured universes and which one is selected.</summary>
public interface IProfileStore
{
    /// <summary>False until the profiles file has been written for the first time.</summary>
    bool Exists { get; }

    IReadOnlyList<UniverseProfile> GetAll();

    UniverseProfile? Get(string name);

    void Save(UniverseProfile profile);

    void Delete(string name);

    string? SelectedName { get; set; }

    event EventHandler? Changed;
}

public sealed class JsonProfileStore(IAppPaths paths) : IProfileStore
{
    private readonly object _sync = new();
    private ProfilesDocument? _document;

    public event EventHandler? Changed;

    public bool Exists => File.Exists(paths.ProfilesFile);

    public IReadOnlyList<UniverseProfile> GetAll()
    {
        lock (_sync)
            return Load().Profiles.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    public UniverseProfile? Get(string name)
    {
        lock (_sync)
            return Load().Profiles.FirstOrDefault(p => p.Name == name);
    }

    public void Save(UniverseProfile profile)
    {
        if (!UniverseProfile.IsValidName(profile.Name))
            throw new ArgumentException($"'{profile.Name}' is not a valid universe name.", nameof(profile));

        Update(document =>
        {
            document.Profiles.RemoveAll(p => p.Name == profile.Name);
            document.Profiles.Add(profile);
            document.Selected ??= profile.Name;
        });
    }

    public void Delete(string name) => Update(document =>
    {
        document.Profiles.RemoveAll(p => p.Name == name);
        if (document.Selected == name)
            document.Selected = document.Profiles.FirstOrDefault()?.Name;
    });

    public string? SelectedName
    {
        get
        {
            lock (_sync)
                return Load().Selected;
        }
        set => Update(document => document.Selected = value);
    }

    private void Update(Action<ProfilesDocument> change)
    {
        lock (_sync)
        {
            var document = Load();
            change(document);
            JsonFile.Write(paths.ProfilesFile, document);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ProfilesDocument Load() => _document ??= JsonFile.Read<ProfilesDocument>(paths.ProfilesFile) ?? new ProfilesDocument();

    private sealed class ProfilesDocument
    {
        public string? Selected { get; set; }
        public List<UniverseProfile> Profiles { get; set; } = [];
    }
}
