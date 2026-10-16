using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.App.Infrastructure;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Storage;
using OgameSkaner.Core.Storage.Legacy;

namespace OgameSkaner.App.ViewModels;

/// <summary>Add, edit and delete universes; import data from the old version of the app.</summary>
public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly IProfileStore _profiles;
    private readonly ISessionStore _sessions;
    private readonly LegacyImporter _legacyImporter;
    private readonly IDialogService _dialogs;

    public ProfilesViewModel(IProfileStore profiles, ISessionStore sessions, LegacyImporter legacyImporter, IDialogService dialogs)
    {
        _profiles = profiles;
        _sessions = sessions;
        _legacyImporter = legacyImporter;
        _dialogs = dialogs;
        Reload(null);
    }

    public ObservableCollection<UniverseProfile> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private UniverseProfile? _selectedProfile;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _galaxies = 9;

    [ObservableProperty]
    private int _systems = 499;

    partial void OnSelectedProfileChanged(UniverseProfile? value)
    {
        if (value is null)
            return;

        Name = value.Name;
        Galaxies = value.Galaxies;
        Systems = value.Systems;
    }

    [RelayCommand]
    private void New()
    {
        SelectedProfile = null;
        Name = string.Empty;
        Galaxies = 9;
        Systems = 499;
    }

    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim().ToLowerInvariant();
        if (!UniverseProfile.IsValidName(name))
        {
            _dialogs.ShowError("The universe name is the sub-domain of ogamex.net, e.g. \"tron\" for tron.ogamex.net.");
            return;
        }

        if (Galaxies < 1 || Systems < 1)
        {
            _dialogs.ShowError("Galaxies and systems must be at least 1.");
            return;
        }

        _profiles.Save(new UniverseProfile(name) { Galaxies = Galaxies, Systems = Systems });
        Reload(name);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedProfile is not { } profile || !_dialogs.Confirm($"Delete universe '{profile.Name}' and its saved session? Scan results are kept on disk."))
            return;

        _profiles.Delete(profile.Name);
        _sessions.Delete(profile.Name);
        Reload(null);
    }

    private bool CanDelete() => SelectedProfile is not null;

    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        var folder = _dialogs.PickFolder("Select the folder of the old OgameSkaner (contains GamesConfiguration.xml)");
        if (folder is null)
            return;

        if (!LegacyImporter.ContainsLegacyData(folder))
        {
            _dialogs.ShowError($"{LegacyImporter.ConfigFileName} was not found in {folder}.");
            return;
        }

        var result = await _legacyImporter.ImportAsync(folder);
        Reload(SelectedProfile?.Name);
        _dialogs.ShowInfo(DescribeImport(result));
    }

    public static string DescribeImport(LegacyImportResult result)
    {
        var message = result.ImportedUniverses.Count == 0
            ? "No new universes found."
            : $"Imported {string.Join(", ", result.ImportedUniverses)} with {result.ImportedPlanets:N0} planets.";

        if (result.SkippedUniverses.Count > 0)
            message += $"{Environment.NewLine}Skipped (already configured): {string.Join(", ", result.SkippedUniverses)}.";

        return message;
    }

    private void Reload(string? selectName)
    {
        Profiles.Clear();
        foreach (var profile in _profiles.GetAll())
            Profiles.Add(profile);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == selectName) ?? Profiles.FirstOrDefault();
        if (SelectedProfile is null)
            New();
    }
}
