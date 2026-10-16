namespace OgameSkaner.Core.Ogamex;

/// <summary>Settings for talking to OgameX, bound from the "Ogamex" configuration section.</summary>
public sealed class OgamexOptions
{
    public const string SectionName = "Ogamex";

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public string AcceptLanguage { get; set; } = "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7";

    /// <summary>Value of the "lang" cookie.</summary>
    public string Language { get; set; } = "pl";

    /// <summary>Value of the "timeZoneOffset" cookie, in minutes.</summary>
    public int TimeZoneOffsetMinutes { get; set; } = 120;

    /// <summary>Minimum delay between two requests to the server.</summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Random extra delay (0..value) added to <see cref="MinRequestInterval"/>.</summary>
    public TimeSpan RequestJitter { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>How many buildings the server allows in a planet's build queue, counting the one being built (1 + 14 waiting).</summary>
    public int BuildQueueCapacity { get; set; } = 15;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>When set, every response body is written to this directory (useful for capturing test fixtures).</summary>
    public string? DumpResponsesDirectory { get; set; }
}
