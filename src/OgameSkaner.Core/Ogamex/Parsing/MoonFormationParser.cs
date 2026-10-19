using System.Globalization;
using System.Text.RegularExpressions;
using OgameSkaner.Core.Domain.Moons;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Reads <c>/home/moonformation</c>: the page script's <c>window.gravitonLevel</c>, <c>currentMoonDiameter</c>
/// (0 = the planet has no moon yet), <c>minValueForDiameter</c> and <c>maxValueForDiameter</c>, and the planet's metal.
/// </summary>
public static partial class MoonFormationParser
{
    /// <exception cref="SessionExpiredException">The content is not the moon formation page.</exception>
    public static MoonFormationInfo Parse(string? html)
    {
        html ??= string.Empty;
        if (!BuildingPageParser.TryReadPage(html, out var planet, out var script) || !html.Contains("currentMoonDiameter", StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return the moon formation page. Check the token and session id.");

        return new MoonFormationInfo(
            planet,
            Read(html, "gravitonLevel") ?? 0,
            Read(html, "currentMoonDiameter") ?? 0,
            Read(html, "minValueForDiameter") ?? MoonFormation.MinDiameter,
            Read(html, "maxValueForDiameter") ?? MoonFormation.MaxDiameter,
            (long)BuildingPageParser.ReadNumber(script, "metal", 0));
    }

    private static int? Read(string html, string name)
    {
        var match = Regex.Match(html, $@"window\.{name}\s*=\s*['`""]?(-?\d+)", RegexOptions.CultureInvariant);
        return match.Success ? int.Parse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : null;
    }
}
