using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.App.Infrastructure;

/// <summary>A window's position and size; <see cref="Width"/>/<see cref="Height"/> are the restored size when maximized.</summary>
public sealed record WindowLayout(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>A length as WPF keeps it: the value and its unit (the enum's number of GridUnitType or DataGridLengthUnitType).</summary>
public sealed record LengthLayout(double Value, int Unit);

/// <summary>A grid's column widths and row heights, which its splitters change.</summary>
public sealed record GridLayout(IReadOnlyList<LengthLayout> Columns, IReadOnlyList<LengthLayout> Rows);

/// <summary>A data grid's column widths and their order on screen.</summary>
public sealed record DataGridColumnLayout(LengthLayout Width, int DisplayIndex);

public sealed class LayoutData
{
    public Dictionary<string, WindowLayout> Windows { get; init; } = [];
    public Dictionary<string, GridLayout> Grids { get; init; } = [];
    public Dictionary<string, List<DataGridColumnLayout>> DataGrids { get; init; } = [];
}

/// <summary>
/// The sizes the user gave the window, the splitters and the data grid columns (<c>layout.json</c>, shared by all
/// universes), written whenever a page closes and when the app exits. A layout that can't be read starts over. Used
/// on the UI thread only.
/// </summary>
public sealed class LayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _file;
    private readonly ILogger<LayoutStore> _logger;

    public LayoutStore(IAppPaths paths, ILogger<LayoutStore> logger)
    {
        _file = Path.Combine(paths.Root, "layout.json");
        _logger = logger;
        Data = Load();
    }

    public LayoutData Data { get; }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Data, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the layout to {File} failed", _file);
        }
    }

    private LayoutData Load()
    {
        try
        {
            if (File.Exists(_file))
                return JsonSerializer.Deserialize<LayoutData>(File.ReadAllText(_file), JsonOptions) ?? new LayoutData();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Reading the layout from {File} failed", _file);
        }

        return new LayoutData();
    }
}
