using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>A building as shown on <c>/building/resource</c> or <c>/building/facility</c>.</summary>
/// <param name="NextLevelCost">Cost of the next level to queue (after the queue); null when the building is locked.</param>
/// <param name="Duration">Construction time of that next level with the planet's current factories.</param>
public sealed record BuildingOnPage(BuildingType Building, int Level, ResourceSet? NextLevelCost, TimeSpan? Duration = null)
{
    public bool Locked => NextLevelCost is null;
}

/// <summary>Everything read from one building page.</summary>
/// <param name="FreeFields">Free planet fields with the queue already counted; only on the resources page.</param>
/// <param name="ProductionPerHour">Resource production per hour (the page script gives it per second).</param>
/// <param name="RunningRemaining">Time left on the running queue item (<c>window.currentConstructionRemainingSeconds</c>).</param>
public sealed record BuildingPage(
    PlanetId CurrentPlanet,
    IReadOnlyList<OwnPlanet> Planets,
    ResourceSet Resources,
    ResourceSet StorageCapacity,
    double EnergyProduction,
    double EnergyConsumption,
    IReadOnlyList<BuildingOnPage> Buildings,
    IReadOnlyList<QueuedBuilding> Queue,
    int? FreeFields,
    bool VacationMode,
    ResourceSet ProductionPerHour,
    TimeSpan? RunningRemaining = null);

/// <summary>
/// Parses the building pages. Resources and energy come from the page's <c>var resources = {…}</c> script
/// (exact numbers), buildings from the <c>div.building-detail</c> blocks, the queue from the
/// "Buildings under construction" box and the planet list from the <c>#planet-selection</c> sidebar.
/// Buildings and queue entries with ids we don't automate (e.g. <c>MOON_BASE</c>) are skipped.
/// </summary>
public static partial class BuildingPageParser
{
    /// <exception cref="SessionExpiredException">The content is not a building page (e.g. a login redirect).</exception>
    public static BuildingPage Parse(string? html)
    {
        html ??= string.Empty;
        if (!TryReadPage(html, out var planetId, out var script) || !html.Contains("buildings-container", StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return a building page. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);

        return new BuildingPage(
            planetId,
            ParsePlanets(document),
            new ResourceSet((long)ReadNumber(script, "metal"), (long)ReadNumber(script, "crystal"), (long)ReadNumber(script, "deuterium")),
            new ResourceSet((long)ReadNumber(script, "metalStorageCapacity"), (long)ReadNumber(script, "crystalStorageCapacity"), (long)ReadNumber(script, "deuteriumStorageCapacity")),
            ReadNumber(script, "energy"),
            ReadNumber(script, "energyConsumption"),
            ParseBuildings(document),
            ParseQueue(document),
            FreeFieldsPattern().Match(html) is { Success: true } fields ? Int(fields.Groups[1].Value) : null,
            VacationPattern().Match(html) is { Success: true } vacation && vacation.Groups[1].Value == "true",
            new ResourceSet(
                (long)Math.Round(ReadNumber(script, "metalProduction") * 3600),
                (long)Math.Round(ReadNumber(script, "crystalProduction") * 3600),
                (long)Math.Round(ReadNumber(script, "deuteriumProduction") * 3600)),
            RemainingPattern().Match(html) is { Success: true } remaining ? TimeSpan.FromSeconds(Int(remaining.Groups[1].Value)) : null);
    }

    /// <summary>Reads the current planet id and the body of the <c>var resources</c> object present on every game page.</summary>
    internal static bool TryReadPage(string html, out PlanetId planet, out string resourcesScript)
    {
        var planetId = CurrentPlanetPattern().Match(html);
        var resources = ResourcesScriptPattern().Match(html);
        planet = planetId.Success ? new PlanetId(planetId.Groups[1].Value) : default;
        resourcesScript = resources.Success ? resources.Groups[1].Value : string.Empty;
        return planetId.Success && resources.Success;
    }

    /// <summary>
    /// Parses the game's time format <c>[Nd ][hh:]mm:ss</c> (see <c>ConvertToTimeString</c> in the game's default.js):
    /// "06:57" is 6 minutes 57 seconds, "01:02:03" one hour, "2d 01:02:03" two days more.
    /// </summary>
    internal static TimeSpan? ParseDuration(string? text)
    {
        var match = DurationPattern().Match(text?.Trim() ?? string.Empty);
        if (!match.Success)
            return null;

        int Part(string group) => match.Groups[group].Success ? Int(match.Groups[group].Value) : 0;
        return new TimeSpan(Part("d"), Part("h"), Part("m"), Part("s"));
    }

    /// <summary>The first time value in the block's <c>div.production-info</c> ("Construction/Production duration : 06:57").</summary>
    internal static TimeSpan? ParseInfoDuration(HtmlNode detail) =>
        ByClass(detail, "div", "info-item")
            .SelectMany(item => item.Descendants("span"))
            .Select(span => ParseDuration(span.InnerText))
            .FirstOrDefault(d => d is not null);

    /// <summary>
    /// Only the planets from the sidebar (present on every game page). Moons have different buildings and get
    /// their own automation later, so any link marked as a moon is skipped.
    /// </summary>
    public static IReadOnlyList<OwnPlanet> ParsePlanets(HtmlDocument document) =>
        ByClass(document.DocumentNode, "a", "planet-select")
            .Where(link => !IsMoon(link))
            .Select(link =>
            {
                var id = PlanetHrefPattern().Match(link.GetAttributeValue("href", string.Empty));
                var name = ByClass(link, "span", "planet-name").FirstOrDefault()?.InnerText.Trim();
                var coordinates = CoordinatesPattern().Match(ByClass(link, "span", "planet-coords").FirstOrDefault()?.InnerText ?? string.Empty);
                return id.Success && name is not null && coordinates.Success
                    ? new OwnPlanet(
                        new PlanetId(id.Groups[1].Value),
                        HtmlEntity.DeEntitize(name),
                        new Coordinates(Int(coordinates.Groups[1].Value), Int(coordinates.Groups[2].Value), Int(coordinates.Groups[3].Value)))
                    : null;
            })
            .OfType<OwnPlanet>()
            .ToList();

    private static bool IsMoon(HtmlNode link) =>
        link.AncestorsAndSelf().Take(3).Any(n => n.GetClasses().Any(c => c.Contains("moon", StringComparison.OrdinalIgnoreCase)));

    private static List<BuildingOnPage> ParseBuildings(HtmlDocument document)
    {
        var buildings = new List<BuildingOnPage>();
        foreach (var detail in ByClass(document.DocumentNode, "div", "building-detail"))
        {
            if (!BuildingCatalog.TryGetByServerId(detail.GetAttributeValue("data-building-type", null), out var building))
                continue;

            var levelText = ByClass(detail, "h3", "building-level").FirstOrDefault()?.InnerText ?? string.Empty;
            var level = int.TryParse(Digits(levelText), NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? l : 0;
            buildings.Add(new BuildingOnPage(building, level, ParseCost(detail), ParseInfoDuration(detail)));
        }

        return buildings;
    }

    /// <summary>Reads the "Required resources" items; null when there are none (locked building).</summary>
    internal static ResourceSet? ParseCost(HtmlNode detail)
    {
        var items = ByClass(detail, "div", "required-resource-item").ToList();
        if (items.Count == 0)
            return null;

        long metal = 0, crystal = 0, deuterium = 0;
        foreach (var item in items)
        {
            var icon = ByClass(item, "span", "resource-icon").FirstOrDefault()?.GetAttributeValue("style", string.Empty) ?? string.Empty;
            var amountText = ByClass(item, "span", "resource-amount").FirstOrDefault()?.InnerText ?? string.Empty;
            var amount = long.TryParse(Digits(amountText), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : 0;

            if (icon.Contains("/metal.", StringComparison.Ordinal))
                metal = amount;
            else if (icon.Contains("/crystal.", StringComparison.Ordinal))
                crystal = amount;
            else if (icon.Contains("/deuterium.", StringComparison.Ordinal))
                deuterium = amount;
        }

        return new ResourceSet(metal, crystal, deuterium);
    }

    /// <summary>The running item (<c>#firstConstruction</c>) followed by the waiting ones in order.</summary>
    private static List<QueuedBuilding> ParseQueue(HtmlDocument document)
    {
        var queue = new List<QueuedBuilding>();
        if (document.GetElementbyId("firstConstruction") is { } running && ToQueued(running, started: true) is { } first)
            queue.Add(first);

        queue.AddRange(ByClass(document.DocumentNode, "a", "construction-queue-item")
            .Select(item => ToQueued(item, started: false))
            .OfType<QueuedBuilding>());
        return queue;
    }

    private static QueuedBuilding? ToQueued(HtmlNode node, bool started) =>
        BuildingCatalog.TryGetByServerId(node.GetAttributeValue("data-building-type", null), out var building)
        && int.TryParse(node.GetAttributeValue("data-tolevel", null), NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            ? new QueuedBuilding(building, level, started)
            : null;

    /// <summary>Reads <c>name: 123.45</c> from the body of the <c>var resources</c> object.</summary>
    internal static double ReadNumber(string script, string name)
    {
        var match = Regex.Match(script,$@"\b{name}:\s*(-?[0-9.]+)", RegexOptions.CultureInvariant);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new OgamexException($"Building page is missing the '{name}' value.");
    }

    internal static IEnumerable<HtmlNode> ByClass(HtmlNode root, string tag, string cssClass) =>
        root.Descendants(tag).Where(n => n.GetClasses().Contains(cssClass));

    private static string Digits(string text) => new(text.Where(char.IsAsciiDigit).ToArray());

    internal static int Int(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"window\.currentPlanetId\s*=\s*['`""]([^'`""]+)['`""]")]
    private static partial Regex CurrentPlanetPattern();

    [GeneratedRegex(@"^(?:(?<d>\d+)d\s+)?(?:(?<h>\d{1,2}):)?(?<m>\d{1,2}):(?<s>\d{2})$")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"remainingFieldsQueueIncluded\s*=\s*['`""]?(\d+)")]
    private static partial Regex FreeFieldsPattern();

    [GeneratedRegex(@"window\.currentConstructionRemainingSeconds\s*=\s*(\d+)")]
    private static partial Regex RemainingPattern();

    [GeneratedRegex(@"window\.isVacation\s*=\s*(true|false)")]
    private static partial Regex VacationPattern();

    [GeneratedRegex(@"var\s+resources\s*=\s*\{(.*?)\}", RegexOptions.Singleline)]
    private static partial Regex ResourcesScriptPattern();

    [GeneratedRegex(@"[?&]planet=([0-9a-fA-F-]+)")]
    private static partial Regex PlanetHrefPattern();

    [GeneratedRegex(@"\[(\d+):(\d+):(\d+)\]")]
    private static partial Regex CoordinatesPattern();
}
