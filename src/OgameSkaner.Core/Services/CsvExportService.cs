using System.Globalization;
using CsvHelper;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Services;

/// <summary>Exports planets to CSV, one row per planet ordered by player name.</summary>
public sealed class CsvExportService
{
    public async Task ExportAsync(IEnumerable<PlayerPlanet> planets, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await ExportAsync(planets, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportAsync(IEnumerable<PlayerPlanet> planets, Stream stream, CancellationToken cancellationToken = default)
    {
        var rows = planets
            .OrderBy(p => p.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Coordinates.Galaxy)
            .ThenBy(p => p.Coordinates.System)
            .ThenBy(p => p.Coordinates.Position)
            .Select(p => new CsvRow(
                p.PlayerName,
                p.Coordinates.Galaxy,
                p.Coordinates.System,
                p.Coordinates.Position,
                p.Coordinates.ToString(),
                p.HasMoon));

        await using var writer = new StreamWriter(stream, leaveOpen: true);
        await using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    private sealed record CsvRow(string UserName, int Galaxy, int SolarSystem, int Position, string Coordinates, bool HasMoon);
}
