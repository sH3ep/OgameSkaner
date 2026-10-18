using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Asteroids;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses the asteroid parts of the galaxy: the "Find asteroids" hint (<c>/galaxy/Partial_AsteroidLocation</c>, a table
/// whose rows read <c>[g:from:17] ? [g:to:17]</c>) and position 17 of a galaxy page (<c>a.btn-asteroid</c> with the
/// seconds left in <c>data-asteroid-disappear</c>). See docs/ogamex-asteroid-api.md.
/// </summary>
public static partial class AsteroidParser
{
    public const int AsteroidPosition = 17;

    private const string HintTableId = "playerAsteroidTable";

    /// <summary>The hint's rows in page order; a row may repeat (one row per asteroid).</summary>
    /// <exception cref="SessionExpiredException">The content is not the hint (e.g. a login redirect).</exception>
    public static IReadOnlyList<AsteroidHint> ParseHints(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var table = document.DocumentNode.Descendants("table").FirstOrDefault(t => t.Id == HintTableId);
        if (table is null)
        {
            // Without asteroids the modal may have no table, but still its description.
            if (html is not null && html.Contains("galaxy-asteroid-modal", StringComparison.Ordinal))
                return [];
            throw new SessionExpiredException("The server did not return the asteroid locations. Check the token and session id.");
        }

        var hints = new List<AsteroidHint>();
        foreach (var row in table.Descendants("tr"))
        {
            var ends = CoordinatesPattern().Matches(row.InnerText);
            if (ends.Count < 2)
                continue;
            var from = (Galaxy: int.Parse(ends[0].Groups[1].Value), System: int.Parse(ends[0].Groups[2].Value));
            var to = (Galaxy: int.Parse(ends[1].Groups[1].Value), System: int.Parse(ends[1].Groups[2].Value));
            if (from.Galaxy != to.Galaxy)
                continue;
            hints.Add(new AsteroidHint(from.Galaxy, Math.Min(from.System, to.System), Math.Max(from.System, to.System)));
        }

        return hints;
    }

    /// <summary>The asteroid at position 17 of a galaxy page, or null when there is none.</summary>
    /// <exception cref="SessionExpiredException">The content is not a galaxy page (e.g. a login redirect).</exception>
    public static Asteroid? ParseGalaxy(string? html, SolarSystemAddress address, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<span class=\"planet-index\">", StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return a galaxy page. Check the token and session id.");

        if (AsteroidPattern().Match(html) is not { Success: true } match)
            return null;

        var seconds = long.Parse(match.Groups[1].Value);
        return new Asteroid(new Coordinates(address.Galaxy, address.System, AsteroidPosition), now + TimeSpan.FromSeconds(seconds), now);
    }

    /// <summary>
    /// The mining reports of one <c>/messages/messagedata?MessageCategoryType=FLEET_OTHER</c> page, newest first. A
    /// report with resources has a line per resource, <c>&lt;img src=".../resource/metal.png"&gt; : &lt;span&gt;gained&lt;/span&gt; / held</c>.
    /// The other results are text only: a number means dark matter; "container" (English labels) a container; else nothing.
    /// </summary>
    /// <param name="serverOffset">Time zone of the dates shown.</param>
    /// <exception cref="SessionExpiredException">The content is not a messages fragment (e.g. a login redirect).</exception>
    public static IReadOnlyList<AsteroidReport> ParseReports(string? html, TimeSpan serverOffset)
    {
        if (string.IsNullOrEmpty(html) || (!html.Contains("message-item", StringComparison.Ordinal) && !html.Contains("No messages found", StringComparison.Ordinal)))
            throw new SessionExpiredException("The server did not return the messages. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var reports = new List<AsteroidReport>();
        foreach (var item in document.DocumentNode.Descendants("div").Where(d => d.GetAttributeValue("data-msg-template", null) == ReportTemplate))
        {
            if (TryParseReport(item, serverOffset) is { } report)
                reports.Add(report);
        }

        return reports;
    }

    private const string ReportTemplate = "FLEET_ASTEROID_MINING_REPORT";

    private static AsteroidReport? TryParseReport(HtmlNode item, TimeSpan serverOffset)
    {
        var id = item.GetAttributeValue("data-msg-id", null);
        var head = item.Descendants("div").FirstOrDefault(d => d.HasClass("head-left"));
        var time = item.Descendants("div").FirstOrDefault(d => d.HasClass("head-right")) is { } right ? TimePattern().Match(right.InnerText) : null;
        var coordinates = head is null ? null : FullCoordinatesPattern().Match(head.InnerText);
        if (string.IsNullOrEmpty(id) || coordinates is not { Success: true } || time is not { Success: true }
            || !DateTime.TryParseExact(time.Value, "dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var shown))
            return null;

        var target = new Coordinates(int.Parse(coordinates.Groups[1].Value), int.Parse(coordinates.Groups[2].Value), int.Parse(coordinates.Groups[3].Value));
        var content = item.Descendants("div").FirstOrDefault(d => d.HasClass("message-content"));
        var lines = content?.Descendants("img")
            .Select(img => (Resource: ResourceName(img.GetAttributeValue("src", "")), Line: img.ParentNode))
            .Where(r => r.Resource is not null)
            .ToList() ?? [];

        long Gained(string resource) => lines.Where(l => l.Resource == resource).Sum(l => Amounts(l.Line).Gained);

        AsteroidResultKind kind;
        long available = 0, darkMatter = 0;
        var gained = ResourceSet.Zero;
        var text = HtmlEntity.DeEntitize(content?.InnerText ?? "");
        if (lines.Any(l => l.Resource != "darkmatter"))
        {
            kind = AsteroidResultKind.Resources;
            available = lines.Where(l => l.Resource != "darkmatter").Sum(l => Amounts(l.Line).Held);
            gained = new ResourceSet(Gained("metal"), Gained("crystal"), Gained("deuterium"));
        }
        else if (lines.Count > 0 || NumberPattern().Match(text) is { Success: true })
        {
            kind = AsteroidResultKind.DarkMatter;
            darkMatter = lines.Count > 0 ? Gained("darkmatter") : FleetPageParser.ParseNumber(NumberPattern().Match(text).Value);
        }
        else
        {
            kind = text.Contains("container", StringComparison.OrdinalIgnoreCase) ? AsteroidResultKind.Container : AsteroidResultKind.Nothing;
        }

        return new AsteroidReport(id, new DateTimeOffset(shown, serverOffset), target, kind, available, gained, darkMatter);
    }

    /// <summary>metal, crystal, deuterium or darkmatter from <c>/assets/images/resource/metal.png</c>; null for other images.</summary>
    private static string? ResourceName(string src)
    {
        var match = ResourceImagePattern().Match(src);
        if (!match.Success)
            return null;
        var name = match.Groups[1].Value.ToLowerInvariant().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return name is "metal" or "crystal" or "deuterium" ? name : name.Contains("dark", StringComparison.Ordinal) ? "darkmatter" : null;
    }

    /// <summary><c>: &lt;span&gt;587.500&lt;/span&gt; / 186.276.153.246</c>: the gained and held amounts (held 0 when not shown).</summary>
    private static (long Gained, long Held) Amounts(HtmlNode line)
    {
        var numbers = NumberPattern().Matches(HtmlEntity.DeEntitize(line.InnerText)).Select(m => FleetPageParser.ParseNumber(m.Value)).ToList();
        return (numbers.ElementAtOrDefault(0), numbers.ElementAtOrDefault(1));
    }

    [GeneratedRegex(@"\[(\d+):(\d+):\d+\]")]
    private static partial Regex CoordinatesPattern();

    [GeneratedRegex(@"\[(\d+):(\d+):(\d+)\]")]
    private static partial Regex FullCoordinatesPattern();

    [GeneratedRegex(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"/resource/([A-Za-z_-]+)\.(?:png|gif|jpg)", RegexOptions.IgnoreCase)]
    private static partial Regex ResourceImagePattern();

    [GeneratedRegex(@"\d{1,3}(?:[.,]\d{3})+|\d+")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"class=""btn-asteroid"".*?data-asteroid-disappear=""(\d+)""", RegexOptions.Singleline)]
    private static partial Regex AsteroidPattern();
}
