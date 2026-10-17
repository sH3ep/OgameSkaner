using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses espionage reports: the list (<c>/messages/messagedata?MessageCategoryType=FLEET_ESPIONAGE</c>) and a report's
/// detail page (<c>/messages/spyreportdetail?reportid=</c>). Labels follow the language cookie, so everything is found
/// by structure (resource icons, section order, unit ids or English names). See docs/ogamex-raid-api.md.
/// </summary>
public static partial class EspionageReportParser
{
    private const string ReportTemplate = "FLEET_ESPIONAGE_REPORT";
    private const string MessagesMarker = "#messages-container";
    private const string DetailContainerId = "spy-report-modal";
    private const int ShipsSection = 1;
    private const int DefensesSection = 2;

    /// <summary>The reports of one list page, newest first, without ships and defenses.</summary>
    /// <param name="serverOffset">Time zone of the dates shown (the <c>timeZoneOffset</c> cookie).</param>
    /// <exception cref="SessionExpiredException">The content is not a messages fragment (e.g. a login redirect).</exception>
    public static IReadOnlyList<EspionageReport> ParseList(string? html, TimeSpan serverOffset)
    {
        if (string.IsNullOrEmpty(html) || (!html.Contains(MessagesMarker, StringComparison.Ordinal) && !html.Contains("message-item", StringComparison.Ordinal)))
            throw new SessionExpiredException("The server did not return the messages. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var reports = new List<EspionageReport>();
        foreach (var item in document.DocumentNode.Descendants("div").Where(d => d.GetAttributeValue("data-msg-template", null) == ReportTemplate))
        {
            if (TryParseItem(item, serverOffset) is { } report)
                reports.Add(report);
        }

        return reports;
    }

    /// <summary>Ships and defenses of a report's detail page; each is null when the probes didn't see it.</summary>
    /// <exception cref="SessionExpiredException">The content is not a report detail.</exception>
    public static (UnitSection? Ships, UnitSection? Defenses) ParseDetail(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var container = document.GetElementbyId(DetailContainerId)
                        ?? throw new SessionExpiredException("The server did not return the espionage report. Check the token and session id.");

        var sections = container.Descendants("div").Where(d => d.HasClass("expand-item")).ToList();
        return (ReadUnits(sections.ElementAtOrDefault(ShipsSection)), ReadUnits(sections.ElementAtOrDefault(DefensesSection)));
    }

    private static EspionageReport? TryParseItem(HtmlNode item, TimeSpan serverOffset)
    {
        var id = item.GetAttributeValue("data-msg-id", null);
        var head = item.Descendants("div").FirstOrDefault(d => d.HasClass("head-left"));
        var coordinates = head is null ? null : CoordinatesPattern().Match(head.InnerText);
        if (string.IsNullOrEmpty(id) || coordinates is not { Success: true })
            return null;

        var target = new Coordinates(Int(coordinates.Groups[1].Value), Int(coordinates.Groups[2].Value), Int(coordinates.Groups[3].Value));
        var player = item.Descendants("span").FirstOrDefault(s => s.HasClass("msg-player-status"));

        return new EspionageReport(
            id,
            target,
            ReadTargetType(item),
            ReadTime(item, serverOffset),
            player is null ? null : WebUtility.HtmlDecode(player.InnerText).Trim(),
            player is null ? null : ReadBetween(player.GetAttributeValue("onclick", ""), "('", "'"),
            player is null ? PlayerStatus.None : PlayerStatusExtensions.FromCssClasses(player.GetClasses()),
            new ResourceSet(ReadResource(item, "metal"), ReadResource(item, "crystal"), ReadResource(item, "deuterium")),
            ReadCounterEspionage(item));
    }

    /// <summary>The report's attack link carries the target type: <c>/fleet?x=8&amp;y=250&amp;z=10&amp;planet=1&amp;mission=8</c>.</summary>
    private static PlanetType ReadTargetType(HtmlNode item)
    {
        var attack = item.Descendants("a").FirstOrDefault(a => a.HasClass("btn-msg-attack"));
        var match = PlanetParameterPattern().Match(WebUtility.HtmlDecode(attack?.GetAttributeValue("href", "") ?? ""));
        return match.Success && match.Groups[1].Value == "2" ? PlanetType.Moon : PlanetType.Planet;
    }

    private static DateTimeOffset ReadTime(HtmlNode item, TimeSpan serverOffset)
    {
        var right = item.Descendants("div").FirstOrDefault(d => d.HasClass("head-right"));
        var match = right is null ? null : DatePattern().Match(right.InnerText);
        return match is { Success: true }
               && DateTime.TryParseExact(match.Value, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? new DateTimeOffset(time, serverOffset)
            : DateTimeOffset.MinValue;
    }

    /// <summary><c>&lt;img src="/assets/images/resource/metal.png" …&gt; : 131.233.495.045</c></summary>
    private static long ReadResource(HtmlNode item, string resource)
    {
        var icon = item.Descendants("img").FirstOrDefault(i =>
            i.GetAttributeValue("src", "").EndsWith($"/{resource}.png", StringComparison.OrdinalIgnoreCase));
        return icon?.ParentNode is { } row ? LastNumber(row.InnerText) ?? 0 : 0;
    }

    private static int? ReadCounterEspionage(HtmlNode item)
    {
        var text = item.Descendants("span").Select(s => s.InnerText.Trim()).FirstOrDefault(t => t.EndsWith('%'));
        var match = text is null ? null : PercentPattern().Match(text);
        return match is { Success: true } ? Int(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// A section the probes saw has the section's points in its header (<c>Defenses (&lt;span&gt;2.000&lt;/span&gt;)</c>);
    /// a hidden one has none and only a sentence as content. Unit rows are
    /// <c>&lt;div&gt;&lt;img src=".../defense/MISSILE_LAUNCHER.gif"&gt;&lt;span&gt;Missile Launcher&lt;/span&gt;&lt;span&gt;1.000&lt;/span&gt;&lt;/div&gt;</c>.
    /// </summary>
    private static UnitSection? ReadUnits(HtmlNode? section)
    {
        var header = section?.Descendants("div").FirstOrDefault(d => d.HasClass("header"));
        var points = header?.Descendants("span").Select(s => LastNumber(s.InnerText)).FirstOrDefault(n => n is not null);
        if (section is null || points is null)
            return null; // hidden from the probes

        var content = section.Descendants("div").FirstOrDefault(d => d.HasClass("content"));
        var units = new List<UnitCount>();
        if (content is not null)
        {
            var rows = content.Descendants().Where(n => n.Name is "div" or "li" or "tr" && !n.Descendants().Any(c => c.Name is "div" or "li" or "tr"));
            foreach (var row in rows)
            {
                var text = WebUtility.HtmlDecode(row.InnerText).Trim();
                if (LastNumber(text) is not { } quantity)
                    continue;
                units.Add(new UnitCount(IdentifyUnit(row, text), quantity));
            }
        }

        return new UnitSection(points.Value, units);
    }

    /// <summary>The unit's catalog id from an attribute (id in an image name, data attribute or tooltip) or its English name.</summary>
    private static string IdentifyUnit(HtmlNode row, string text)
    {
        foreach (var node in row.DescendantsAndSelf())
        {
            foreach (var attribute in node.Attributes)
            {
                var value = WebUtility.HtmlDecode(attribute.Value);
                if (attribute.Name is "src")
                    value = Path.GetFileNameWithoutExtension(value.Split('?')[0]);
                else if (attribute.Name == "data-tooltip-content")
                    value = TagPattern().Replace(value, " ").Trim();

                if (value.Length > 0 && value.Length < 60 && UnitCatalog.Find(value) is { } unit)
                    return unit.Id;
            }
        }

        // <span>Light Fighter</span><span>1.000</span>, or "Light Fighter : 1.000"
        var name = row.Descendants("span")
                       .Select(s => WebUtility.HtmlDecode(s.InnerText).Trim())
                       .FirstOrDefault(t => t.Length > 0 && NumberPattern().Match(t).Value != t)
                   ?? NumberPattern().Replace(text, "").Trim(' ', ':');
        return UnitCatalog.Find(name)?.Id ?? name;
    }

    private static long? LastNumber(string text)
    {
        var matches = NumberPattern().Matches(text);
        return matches.Count == 0 ? null : FleetPageParser.ParseNumber(matches[^1].Value);
    }

    private static string? ReadBetween(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
            return null;
        from += start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? null : text[from..to];
    }

    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[(\d+):(\d+):(\d+)\]")]
    private static partial Regex CoordinatesPattern();

    [GeneratedRegex(@"[?&]planet=(\d)")]
    private static partial Regex PlanetParameterPattern();

    [GeneratedRegex(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(\d+)\s*%")]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex NumberPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();
}
