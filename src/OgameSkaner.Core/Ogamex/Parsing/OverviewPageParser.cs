using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses the overview page (<c>/home</c>) for the planet's temperature. The planet properties are
/// <c>div.planet-prop</c> blocks with a <c>.prop-text</c> such as "Approximately 69°C to 74°C"; the temperature is the
/// one with two °C values, so the label's language doesn't matter.
/// </summary>
public static partial class OverviewPageParser
{
    /// <exception cref="SessionExpiredException">The content is not a game page.</exception>
    /// <exception cref="OgamexException">The page has no temperature.</exception>
    public static (int Min, int Max) ParseTemperature(string? html)
    {
        html ??= string.Empty;
        if (!BuildingPageParser.TryReadPage(html, out _, out _))
            throw new SessionExpiredException("The server did not return the overview page. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var prop in BuildingPageParser.ByClass(document.DocumentNode, "div", "planet-prop"))
        {
            var match = TemperaturePattern().Match(HtmlEntity.DeEntitize(prop.InnerText));
            if (match.Success)
                return (Degrees(match.Groups[1].Value), Degrees(match.Groups[2].Value));
        }

        throw new OgamexException("The overview page shows no planet temperature.");
    }

    private static int Degrees(string text) =>
        int.Parse(text.Replace('−', '-'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"([-−]?\d+)\s*°C\D*?([-−]?\d+)\s*°C")]
    private static partial Regex TemperaturePattern();
}
