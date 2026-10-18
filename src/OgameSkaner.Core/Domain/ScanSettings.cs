namespace OgameSkaner.Core.Domain;

/// <summary>
/// The galaxy scanner's pacing, saved per universe. A delay is measured from the previous system's request, so the
/// client's own minimum gap between requests (<c>Ogamex:MinRequestInterval</c>) stays the floor.
/// </summary>
public sealed record ScanSettings
{
    /// <summary>Shortest time between the requests of two systems, in milliseconds (0 = only the request gap).</summary>
    public int MinDelayMilliseconds { get; init; }

    /// <summary>Longest time between the requests of two systems; each delay is picked at random between the two.</summary>
    public int MaxDelayMilliseconds { get; init; }

    /// <summary>A longer pause after every this many systems (0 = no pauses).</summary>
    public int PauseEverySystems { get; init; }

    public int MinPauseSeconds { get; init; }

    public int MaxPauseSeconds { get; init; }

    public static ScanSettings Default { get; } = new();

    /// <summary>Whether the system after <paramref name="scanned"/> systems comes after a pause.</summary>
    public bool PausesAfter(int scanned) => PauseEverySystems > 0 && scanned > 0 && scanned % PauseEverySystems == 0;

    /// <summary>The time between the request of system number <paramref name="scanned"/> and the next one.</summary>
    public TimeSpan DelayAfter(int scanned, Random random) => PausesAfter(scanned)
        ? TimeSpan.FromSeconds(Between(MinPauseSeconds, MaxPauseSeconds, random))
        : TimeSpan.FromMilliseconds(Between(MinDelayMilliseconds, MaxDelayMilliseconds, random));

    private static int Between(int min, int max, Random random)
    {
        min = Math.Max(0, min);
        return random.Next(min, Math.Max(min, max) + 1);
    }
}
