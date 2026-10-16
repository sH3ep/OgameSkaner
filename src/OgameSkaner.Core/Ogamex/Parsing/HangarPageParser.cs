using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses the shipyard page (<c>/hangar</c>) for what the build automation needs: solar satellites.
/// The count comes from the page script (<c>ships['SOLAR_SATELLITE'] = 300</c>), the cost from the
/// <c>data-required-*</c> attributes and the energy per satellite from the description text.
/// </summary>
public static partial class HangarPageParser
{
    public const string SolarSatellite = "SOLAR_SATELLITE";

    /// <exception cref="SessionExpiredException">The content is not a shipyard page.</exception>
    public static ShipyardState Parse(string? html)
    {
        html ??= string.Empty;
        if (!BuildingPageParser.TryReadPage(html, out var planet, out _) || !html.Contains("hangar-detail", StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return the shipyard page. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var detail = document.GetElementbyId("detail-" + SolarSatellite)
                     ?? throw new OgamexException("The shipyard page has no solar satellite.");
        var cost = BuildingPageParser.ParseCost(detail);
        var energy = EnergyPattern().Match(HtmlEntity.DeEntitize(detail.InnerText));

        return new ShipyardState(
            planet,
            SatelliteCountPattern().Match(html) is { Success: true } count ? BuildingPageParser.Int(count.Groups[1].Value) : 0,
            energy.Success ? double.Parse(energy.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture) : 0,
            cost ?? ResourceSet.Zero,
            SatellitesLocked: cost is null,
            BuildingPageParser.ParseInfoDuration(detail));
    }

    [GeneratedRegex(@"ships\['SOLAR_SATELLITE'\]\s*=\s*(\d+)")]
    private static partial Regex SatelliteCountPattern();

    [GeneratedRegex(@"produces\s+([0-9]+(?:\.[0-9]+)?)\s+energy")]
    private static partial Regex EnergyPattern();
}
