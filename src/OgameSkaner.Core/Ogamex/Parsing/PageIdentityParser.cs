namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// The logged-in player on a full game page: its script sets <c>window.currentPlayerId = '&lt;guid&gt;';</c>
/// (building, hangar and other pages; partial and JSON answers don't have it).
/// </summary>
public static class PageIdentityParser
{
    private const string Marker = "window.currentPlayerId = '";

    /// <returns>The player id, or null when the content doesn't name one.</returns>
    public static string? CurrentPlayerId(string? html)
    {
        var start = html?.IndexOf(Marker, StringComparison.Ordinal) ?? -1;
        if (start < 0)
            return null;

        start += Marker.Length;
        var end = html!.IndexOf('\'', start);
        return end > start && end - start <= 64 ? html[start..end] : null;
    }
}
