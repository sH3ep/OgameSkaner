using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// The debris fields of a <c>/galaxy/galaxydata</c> page. A row's <c>col-debris</c> column holds an icon whose tooltip
/// lists the field's resources, one line per resource: its image (<c>/assets/images/resource/metal.png</c>) and the amount
/// (<c>4.956.800</c>). The column of position 16 also shows an icon for own fleets there (<c>fleetActionIcon</c>).
/// Labels follow the language cookie, so this goes by the images. See docs/ogamex-expedition-api.md.
/// </summary>
public static partial class DebrisParser
{
    /// <summary>The debris per position (only positions with a field).</summary>
    public static IReadOnlyDictionary<int, ResourceSet> Parse(string? html)
    {
        var result = new Dictionary<int, ResourceSet>();
        if (string.IsNullOrEmpty(html))
            return result;

        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var column in document.DocumentNode.Descendants("div").Where(d => d.HasClass("col-debris") && !d.HasClass("header-col")))
        {
            if (column.ParentNode?.Descendants("span").FirstOrDefault(s => s.HasClass("planet-index")) is not { } index
                || !int.TryParse(index.InnerText.Trim(), out var position))
                continue;

            var debris = column.Descendants()
                .Select(n => n.GetAttributeValue("data-tooltip-content", null))
                .Where(t => t is not null)
                .Select(t => Resources(HtmlEntity.DeEntitize(t)))
                .FirstOrDefault(r => r is not null);
            if (debris is { } d && d.Metal + d.Crystal + d.Deuterium > 0)
                result[position] = d;
        }

        return result;
    }

    /// <summary>The amounts of a debris tooltip; null when it shows no resource.</summary>
    private static ResourceSet? Resources(string tooltip)
    {
        var lines = ResourceLinePattern().Matches(tooltip);
        if (lines.Count == 0)
            return null;

        long metal = 0, crystal = 0, deuterium = 0;
        foreach (Match line in lines)
        {
            var amount = FleetPageParser.ParseNumber(line.Groups[2].Value);
            switch (line.Groups[1].Value.ToLowerInvariant())
            {
                case "metal": metal = amount; break;
                case "crystal": crystal = amount; break;
                case "deuterium": deuterium = amount; break;
            }
        }

        return new ResourceSet(metal, crystal, deuterium);
    }

    /// <summary>A resource image followed by the amount of the next span.</summary>
    [GeneratedRegex(@"/resource/(metal|crystal|deuterium)\.(?:png|gif|jpg).*?<span[^>]*>\s*([\d.,]+)\s*</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ResourceLinePattern();
}
