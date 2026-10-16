using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OgameSkaner.Core.Domain;

/// <summary>A configured OgameX universe, e.g. "tron" for https://tron.ogamex.net.</summary>
public sealed partial record UniverseProfile(string Name)
{
    public int Galaxies { get; init; } = 9;

    public int Systems { get; init; } = 499;

    [JsonIgnore]
    public Uri BaseUri => new($"https://{Name}.ogamex.net/");

    /// <summary>Universe names become a sub-domain, so only allow safe host labels.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex NamePattern();
}
