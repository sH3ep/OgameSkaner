using System.Globalization;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>Parses the <c>/fleet</c> page: available ships and the coordinates of the current planet.</summary>
public static class FleetPageParser
{
    public const string ShipsContainerId = "fleet1_content_container";

    /// <exception cref="SessionExpiredException">The content is not a fleet page.</exception>
    public static FleetSnapshot Parse(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);

        var container = document.GetElementbyId(ShipsContainerId)
                        ?? throw new SessionExpiredException("The server did not return the fleet page. Check the token and session id.");

        var ships = container.Descendants("a")
            .Select(node => new
            {
                Type = node.GetAttributeValue("data-ship-type", null),
                Quantity = ParseNumber(node.GetAttributeValue("data-ship-quantity", null))
            })
            .Where(x => !string.IsNullOrEmpty(x.Type) && x.Quantity > 0)
            .Select(x => new ShipStack(x.Type!, x.Quantity))
            .ToList();

        var currentPlanet = new Coordinates(
            ReadInput(document, "fleet2_target_x"),
            ReadInput(document, "fleet2_target_y"),
            ReadInput(document, "fleet2_target_z"));

        return new FleetSnapshot(ships, currentPlanet);
    }

    private static int ReadInput(HtmlDocument document, string id)
    {
        var value = document.GetElementbyId(id)?.GetAttributeValue("value", null);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new OgamexException($"Fleet page is missing a valid '{id}' value.");
    }

    private static long ParseNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }
}
