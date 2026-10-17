using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses the <c>/fleet</c> page: available ships with their cargo capacity, the fleet slots and the coordinates of the
/// current planet.
/// </summary>
public static partial class FleetPageParser
{
    public const string ShipsContainerId = "fleet1_content_container";

    /// <exception cref="SessionExpiredException">The content is not a fleet page.</exception>
    public static FleetSnapshot Parse(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);

        var container = document.GetElementbyId(ShipsContainerId)
                        ?? throw new SessionExpiredException("The server did not return the fleet page. Check the token and session id.");

        var ships = new List<ShipStack>();
        var cargo = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var node in container.Descendants("a"))
        {
            var type = node.GetAttributeValue("data-ship-type", null);
            var quantity = ParseNumber(node.GetAttributeValue("data-ship-quantity", null));
            if (string.IsNullOrEmpty(type) || quantity <= 0)
                continue;

            ships.Add(new ShipStack(type, quantity));
            if (ReadCargoCapacity(node) is { } capacity)
                cargo[type] = capacity;
        }

        var currentPlanet = new Coordinates(
            ReadInput(document, "fleet2_target_x"),
            ReadInput(document, "fleet2_target_y"),
            ReadInput(document, "fleet2_target_z"));

        return new FleetSnapshot(ships, currentPlanet, ReadSlots(document), cargo, ReadServerClock(document));
    }

    /// <summary>The clock in the page header ("16.10.2026 20:43:23"), in the zone the game shows dates in.</summary>
    private static DateTime? ReadServerClock(HtmlDocument document)
    {
        var match = DatePattern().Match(document.DocumentNode.InnerText);
        return match.Success && DateTime.TryParseExact(match.Value, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)
            ? clock
            : null;
    }

    /// <summary>"Fleets: 1/11" in <c>.info_item_active_fleets</c>.</summary>
    private static FleetSlots? ReadSlots(HtmlDocument document)
    {
        var node = document.DocumentNode.SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' info_item_active_fleets ')]");
        var match = node is null ? null : SlotsPattern().Match(WebUtility.HtmlDecode(node.InnerText));
        return match is { Success: true }
            ? new FleetSlots(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// The ship tooltip is a table: speed, cargo capacity (one ship, technology included), total cargo capacity. The
    /// labels follow the language cookie, so the second value is taken.
    /// </summary>
    private static long? ReadCargoCapacity(HtmlNode shipButton)
    {
        var item = shipButton.Ancestors().FirstOrDefault(a => a.HasClass("ship-item"));
        var tooltip = item?.GetAttributeValue("data-tooltip-content", null);
        if (string.IsNullOrEmpty(tooltip))
            return null;

        var table = new HtmlDocument();
        table.LoadHtml(WebUtility.HtmlDecode(tooltip));
        var values = table.DocumentNode.Descendants("span").Select(s => s.InnerText.Trim()).Where(t => t.Length > 0).ToList();
        return values.Count >= 3 ? ParseNumber(values[1]) : null;
    }

    private static int ReadInput(HtmlDocument document, string id)
    {
        var value = document.GetElementbyId(id)?.GetAttributeValue("value", null);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new OgamexException($"Fleet page is missing a valid '{id}' value.");
    }

    internal static long ParseNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial Regex SlotsPattern();

    [GeneratedRegex(@"\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}:\d{2}")]
    private static partial Regex DatePattern();
}
