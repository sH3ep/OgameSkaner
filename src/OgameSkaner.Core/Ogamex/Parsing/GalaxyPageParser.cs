using System.Globalization;
using System.Net;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>One occupied position in the galaxy view.</summary>
/// <param name="Rank">The player's ranking from the name tooltip (1 = most points).</param>
public sealed record GalaxyRow(
    int Position,
    string PlayerName,
    bool HasMoon,
    PlayerStatus Status = PlayerStatus.None,
    string? PlayerId = null,
    int? Rank = null);

/// <summary>
/// Parses the HTML returned by <c>/galaxy/galaxydata</c>.
/// The page is processed line by line using markers that are stable in the OgameX markup:
/// every row starts with a <c>col-planet-index</c> column, followed by the position number,
/// the player name and the moon column. Position 16 (expedition) ends the list.
/// The player's line (name span with its tooltip) also carries the status classes (<c>isInactive7</c>, …) and the rank
/// (the <c>/statistics?rel=</c> link in the tooltip); the line before it opens the player badge with the player's id.
/// See docs/ogamex-raid-api.md.
/// </summary>
public static class GalaxyPageParser
{
    private const string RowStartMarker = "<div class=\"galaxy-col col-planet-index";
    private const string PositionMarker = "<span class=\"planet-index\">";
    private const string PlayerNameMarker = "<span style='margin-top:6px;float:left;'>";
    private const string MoonColumnMarker = "<div class=\"galaxy-col col-moon\">";
    private const string SpanEnd = "</span>";
    private const string StatusClassMarker = "class=\"text-area";
    private const string RankLinkMarker = "/statistics?rel=";
    private const string PlayerBadgeMarker = "ShowPlayerBadgeDialog('";
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
        var status = PlayerStatus.None;
        string? playerId = null;
        int? rank = null;

        void FlushRow()
        {
            if (position is > 0 and < ExpeditionPosition && !string.IsNullOrWhiteSpace(playerName))
                rows.Add(new GalaxyRow(position.Value, playerName, hasMoon, status, playerId, rank));

            position = null;
            playerName = null;
            hasMoon = false;
            status = PlayerStatus.None;
            playerId = null;
            rank = null;
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

            if (TryReadBetween(line, PlayerBadgeMarker, "'", out var id))
                playerId = id;

            if (TryReadBetween(line, PlayerNameMarker, SpanEnd, out var name))
            {
                playerName = WebUtility.HtmlDecode(name).Trim();
                if (TryReadBetween(line, StatusClassMarker, "\"", out var classes))
                    status = PlayerStatusExtensions.FromCssClasses(classes.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                rank = ReadRank(line);
            }

            if (line.Contains(MoonColumnMarker, StringComparison.Ordinal))
            {
                var moonLine = reader.ReadLine();
                hasMoon = moonLine is { Length: >= MinMoonLineLength };
            }
        }

        FlushRow();
        return rows;
    }

    /// <summary>The text of the ranking link: <c>href='/statistics?rel=…'>2.471&lt;/a></c>.</summary>
    private static int? ReadRank(string line)
    {
        var link = line.IndexOf(RankLinkMarker, StringComparison.Ordinal);
        if (link < 0)
            return null;
        var start = line.IndexOf('>', link);
        var end = start < 0 ? -1 : line.IndexOf('<', start);
        if (end < 0)
            return null;

        var digits = new string(line[(start + 1)..end].Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var rank) ? rank : null;
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
