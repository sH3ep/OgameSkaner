using System.Net;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>One occupied position in the galaxy view.</summary>
public sealed record GalaxyRow(int Position, string PlayerName, bool HasMoon);

/// <summary>
/// Parses the HTML returned by <c>/galaxy/galaxydata</c>.
/// The page is processed line by line using markers that are stable in the OgameX markup:
/// every row starts with a <c>col-planet-index</c> column, followed by the position number,
/// the player name and the moon column. Position 16 (expedition) ends the list.
/// </summary>
public static class GalaxyPageParser
{
    private const string RowStartMarker = "<div class=\"galaxy-col col-planet-index";
    private const string PositionMarker = "<span class=\"planet-index\">";
    private const string PlayerNameMarker = "<span style='margin-top:6px;float:left;'>";
    private const string MoonColumnMarker = "<div class=\"galaxy-col col-moon\">";
    private const string SpanEnd = "</span>";
    private const int ExpeditionPosition = 16;

    // An empty moon column is followed by a short closing line; a moon renders an image/link.
    private const int MinMoonLineLength = 21;

    /// <exception cref="SessionExpiredException">The content is not a galaxy page (e.g. a login redirect).</exception>
    public static IReadOnlyList<GalaxyRow> Parse(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains(PositionMarker, StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return a galaxy page. Check the token and session id.");

        var rows = new List<GalaxyRow>();
        int? position = null;
        string? playerName = null;
        var hasMoon = false;

        void FlushRow()
        {
            if (position is > 0 and < ExpeditionPosition && !string.IsNullOrWhiteSpace(playerName))
                rows.Add(new GalaxyRow(position.Value, playerName, hasMoon));

            position = null;
            playerName = null;
            hasMoon = false;
        }

        using var reader = new StringReader(html);
        while (reader.ReadLine() is { } line)
        {
            if (line.Contains(RowStartMarker, StringComparison.Ordinal))
                FlushRow();

            if (TryReadPosition(line, out var parsedPosition))
            {
                if (parsedPosition >= ExpeditionPosition)
                    break;
                position = parsedPosition;
            }

            if (TryReadBetween(line, PlayerNameMarker, SpanEnd, out var name))
                playerName = WebUtility.HtmlDecode(name).Trim();

            if (line.Contains(MoonColumnMarker, StringComparison.Ordinal))
            {
                var moonLine = reader.ReadLine();
                hasMoon = moonLine is { Length: >= MinMoonLineLength };
            }
        }

        FlushRow();
        return rows;
    }

    private static bool TryReadPosition(string line, out int position)
    {
        position = 0;
        return TryReadBetween(line, PositionMarker, "<", out var text) && int.TryParse(text.Trim(), out position);
    }

    private static bool TryReadBetween(string line, string startMarker, string endMarker, out string value)
    {
        value = string.Empty;
        var start = line.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return false;

        start += startMarker.Length;
        var end = line.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            return false;

        value = line[start..end];
        return true;
    }
}
