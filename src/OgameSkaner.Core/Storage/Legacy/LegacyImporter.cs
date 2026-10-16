using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Storage.Legacy;

public sealed record LegacyImportResult(IReadOnlyList<string> ImportedUniverses, IReadOnlyList<string> SkippedUniverses, int ImportedPlanets);

/// <summary>
/// Imports data written by the old .NET Framework version of the app:
/// <c>GamesConfiguration.xml</c> (universes + encrypted token) and <c>DatabaseOgameX{universe}.xml</c> (scan results).
/// Only OgameX entries are imported. Universes that already exist are left untouched, so running it twice is safe.
/// </summary>
public sealed class LegacyImporter(
    IProfileStore profiles,
    ISessionStore sessions,
    IPlanetRepository planets,
    ILogger<LegacyImporter> logger)
{
    public const string ConfigFileName = "GamesConfiguration.xml";
    private const string LegacyGameType = "OgameX";
    private const string PlaceholderToken = "token";

    public static bool ContainsLegacyData(string directory) => File.Exists(Path.Combine(directory, ConfigFileName));

    public async Task<LegacyImportResult> ImportAsync(string directory, CancellationToken cancellationToken = default)
    {
        var imported = new List<string>();
        var skipped = new List<string>();
        var planetCount = 0;

        foreach (var entry in ReadConfiguration(Path.Combine(directory, ConfigFileName)))
        {
            if (!UniverseProfile.IsValidName(entry.Universe) || profiles.Get(entry.Universe) is not null)
            {
                skipped.Add(entry.Universe);
                continue;
            }

            profiles.Save(new UniverseProfile(entry.Universe));
            ImportSession(entry);
            planetCount += await ImportPlanetsAsync(directory, entry.Universe, cancellationToken).ConfigureAwait(false);
            imported.Add(entry.Universe);
        }

        logger.LogInformation("Legacy import from {Directory}: imported {Imported}, skipped {Skipped}, {Planets} planets",
            directory, imported, skipped, planetCount);

        return new LegacyImportResult(imported, skipped, planetCount);
    }

    private void ImportSession(LegacyConfiguration entry)
    {
        var token = LegacyTokenDecryptor.TryDecrypt(entry.EncryptedToken);
        if (string.IsNullOrWhiteSpace(token) || token == PlaceholderToken || string.IsNullOrWhiteSpace(entry.SessionId))
            return;

        sessions.Save(entry.Universe, new OgamexSession(token, entry.SessionId));
    }

    private async Task<int> ImportPlanetsAsync(string directory, string universe, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, $"Database{LegacyGameType}{universe}.xml");
        if (!File.Exists(path) || planets.Exists(universe))
            return 0;

        var fallbackDate = new DateTimeOffset(File.GetLastWriteTime(path));
        var legacyPlanets = XDocument.Load(path).Root?.Elements("UserPlanet")
            .Select(e => ToPlanet(e, fallbackDate))
            .OfType<PlayerPlanet>()
            .ToList() ?? [];

        var database = await planets.GetAsync(universe, cancellationToken).ConfigureAwait(false);
        foreach (var system in legacyPlanets.GroupBy(p => p.Coordinates.SolarSystem))
            database.ReplaceSystem(system.Key, system);

        await planets.SaveAsync(database, cancellationToken).ConfigureAwait(false);
        return database.Count;
    }

    private static PlayerPlanet? ToPlanet(XElement element, DateTimeOffset fallbackDate)
    {
        var name = element.Element("UserName")?.Value;
        var galaxy = ReadInt(element, "Galaxy");
        var system = ReadInt(element, "SolarSystem");
        var position = ReadInt(element, "Position");
        if (string.IsNullOrWhiteSpace(name) || galaxy < 1 || system < 1 || position < 1)
            return null;

        var hasMoon = bool.TryParse(element.Element("HasMoon")?.Value, out var moon) && moon;
        var scannedAt = DateTimeOffset.TryParse(element.Element("CreationDate")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : fallbackDate;

        return new PlayerPlanet(name, new Coordinates(galaxy, system, position), hasMoon, scannedAt);
    }

    private static int ReadInt(XElement element, string name) =>
        int.TryParse(element.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static IEnumerable<LegacyConfiguration> ReadConfiguration(string path)
    {
        if (!File.Exists(path))
            return [];

        return XDocument.Load(path).Root?.Elements("GameConfigurationModel")
            .Where(e => e.Element("GameType")?.Value == LegacyGameType)
            .Select(e => new LegacyConfiguration(
                (e.Element("Universum")?.Value ?? string.Empty).Trim().ToLowerInvariant(),
                e.Element("EncryptedToken")?.Value,
                e.Element("SessionId")?.Value))
            .ToList() ?? [];
    }

    private sealed record LegacyConfiguration(string Universe, string? EncryptedToken, string? SessionId);
}
