using System.Text.Json;
using System.Text.Json.Serialization;

namespace OgameSkaner.Core.Storage;

/// <summary>Small helper for reading and atomically writing JSON files.</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>For large data files that nobody edits by hand.</summary>
    public static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options);
    }

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written file behind.</summary>
    public static void Write<T>(string path, T value)
    {
        var temp = PrepareTemp(path);
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, value, Options);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken, JsonSerializerOptions? options = null)
    {
        var temp = PrepareTemp(path);
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, options ?? Options, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static string PrepareTemp(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return path + ".tmp";
    }
}
