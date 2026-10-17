using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses one ranking page (<c>/statistics/statisticsdata?Category=PLAYER&amp;SubCategory=…&amp;Page=…</c>, an HTML
/// fragment). Each row: rank, change, flag, status (<c>span.player-status.isInactive28</c>), the player
/// (<c>ShowPlayerBadgeDialog('&lt;id&gt;')</c>, after an alliance tag if any), message button and the points (first
/// <c>div</c> of the last cell, thousands dots). See docs/ogamex-raid-api.md.
/// </summary>
public static partial class StatisticsPageParser
{
    private const string ContainerClass = "statistics-table-container";

    /// <exception cref="SessionExpiredException">The content is not a ranking (e.g. a login redirect).</exception>
    public static RankingPage Parse(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var container = document.DocumentNode.Descendants("div").FirstOrDefault(d => d.HasClass(ContainerClass))
                        ?? throw new SessionExpiredException("The server did not return the ranking. Check the token and session id.");

        var entries = new List<RankingEntry>();
        foreach (var row in container.Descendants("tbody").SelectMany(b => b.Elements("tr")))
        {
            if (TryParseRow(row) is { } entry)
                entries.Add(entry);
        }

        // The pagination's links (and its select) number the pages; a ranking of one page has none.
        var pages = container.Descendants("a")
            .Select(a => a.GetAttributeValue("data-page-target", null))
            .Select(p => int.TryParse(p, out var page) ? page : 0)
            .DefaultIfEmpty(1)
            .Max();

        return new RankingPage(entries, Math.Max(1, pages));
    }

    private static RankingEntry? TryParseRow(HtmlNode row)
    {
        var cells = row.Elements("td").ToList();
        if (cells.Count < 2 || !int.TryParse(cells[0].InnerText.Trim().Replace(".", "", StringComparison.Ordinal), out var rank))
            return null;

        var player = row.Descendants("a").FirstOrDefault(a => a.GetAttributeValue("onclick", "").Contains("ShowPlayerBadgeDialog", StringComparison.Ordinal));
        if (player is null || PlayerIdPattern().Match(player.GetAttributeValue("onclick", "")) is not { Success: true } id)
            return null;

        var status = row.Descendants("span").FirstOrDefault(s => s.HasClass("player-status"));
        var points = cells[^1].Elements("div").FirstOrDefault()?.InnerText;

        return new RankingEntry(
            rank,
            id.Groups[1].Value,
            HtmlEntity.DeEntitize(player.InnerText).Trim(),
            FleetPageParser.ParseNumber(points),
            status is null ? PlayerStatus.None : PlayerStatusExtensions.FromCssClasses(status.GetClasses()));
    }

    [GeneratedRegex(@"ShowPlayerBadgeDialog\('([^']+)'\)")]
    private static partial Regex PlayerIdPattern();
}
