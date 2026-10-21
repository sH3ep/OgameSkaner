using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>One planet or moon column of the empire page.</summary>
/// <param name="Energy">The planet's energy balance; null for moons.</param>
/// <param name="Diameter">The moon's diameter; null for planets.</param>
/// <param name="ProductionPerHour">Zero for moons (the page has no production row for them).</param>
/// <param name="StorageCapacity">Zero for moons (the page has no storage row for them).</param>
/// <param name="Buildings">Levels by building; buildings the catalog doesn't know are left out.</param>
/// <param name="Ships">Counts by <see cref="Domain.Raiding.UnitCatalog"/> id (<c>LIGHT_CARGO</c>), all types including zeros.</param>
/// <param name="Defenses">Counts by <see cref="Domain.Raiding.UnitCatalog"/> id (<c>MISSILE_LAUNCHER</c>).</param>
public sealed record EmpirePlanet(
    string Name,
    Coordinates Coordinates,
    int UsedFields,
    int MaxFields,
    int MinTemperature,
    int MaxTemperature,
    long? Energy,
    int? Diameter,
    ResourceSet Resources,
    ResourceSet ProductionPerHour,
    ResourceSet StorageCapacity,
    IReadOnlyDictionary<BuildingType, int> Buildings,
    IReadOnlyDictionary<string, long> Ships,
    IReadOnlyDictionary<string, long> Defenses)
{
    public int LevelOf(BuildingType building) => Buildings.GetValueOrDefault(building);

    public long ShipCount(string shipType) => Ships.GetValueOrDefault(shipType);
}

/// <summary>Everything read from the empire page.</summary>
/// <param name="Researches">Research levels by server id (<c>COMPUTER_TECHNOLOGY</c>); they are the account's, read from the first planet.</param>
public sealed record EmpirePage(IReadOnlyList<EmpirePlanet> Planets, IReadOnlyList<EmpirePlanet> Moons, IReadOnlyDictionary<string, int> Researches)
{
    public EmpirePlanet? PlanetAt(Coordinates coordinates) => Planets.FirstOrDefault(p => p.Coordinates == coordinates);

    public EmpirePlanet? MoonAt(Coordinates coordinates) => Moons.FirstOrDefault(m => m.Coordinates == coordinates);
}

/// <summary>
/// Parses <c>/empire</c>: one server-rendered page with every planet and moon side by side (see docs/ogamex-build-api.md).
/// <c>#empire-container</c> holds a <c>.planetViewContainer</c> and a <c>.moonViewContainer</c>; in each, the first row is
/// the column headers (labels, "Sum", then one column per planet: name, coordinates, fields, energy or diameter,
/// temperature) and every <c>.prop-row</c> is a section with one <c>.prop-sub-title</c> per value in column 0 and the
/// values (<c>span.cell-value</c>, "1.234.567") at the same column index. The page has no planet ids, so planets are
/// matched by coordinates.
/// <para>Section titles and labels follow the language cookie, so the sections are told apart by their icons
/// (<c>/assets/images/V2/{building|research|defense|hangar}/{ID}.gif</c>, <c>/assets/images/resource/metal.png</c>):
/// the first resource-icon section is the stock, the second the production (hourly, then daily), a building section with
/// only the three storages the storage capacity, any other building section levels.</para>
/// </summary>
public static partial class EmpirePageParser
{
    private static readonly HashSet<string> Storages = new(StringComparer.Ordinal) { "METAL_STORAGE", "CRYSTAL_STORAGE", "DEUTERIUM_TANK" };

    /// <exception cref="SessionExpiredException">The content is not the empire page (e.g. a login redirect).</exception>
    public static EmpirePage Parse(string? html)
    {
        html ??= string.Empty;
        if (!html.Contains("empire-container", StringComparison.Ordinal))
            throw new SessionExpiredException("The server did not return the empire page. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var researches = new Dictionary<string, int>(StringComparer.Ordinal);
        var planets = ParseView(document, "planetViewContainer", moons: false, researches);
        var moons = ParseView(document, "moonViewContainer", moons: true, researches: null);
        return new EmpirePage(planets, moons, researches);
    }

    private static List<EmpirePlanet> ParseView(HtmlDocument document, string container, bool moons, Dictionary<string, int>? researches)
    {
        var view = BuildingPageParser.ByClass(document.DocumentNode, "div", container).FirstOrDefault();
        if (view is null)
            return [];

        var rows = view.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
        if (rows.Count == 0)
            return [];
        var headers = Columns(rows[0]);
        var sections = rows.Skip(1).Where(r => r.HasClass("prop-row")).Select(Section).ToList();

        var planets = new List<EmpirePlanet>();
        for (var column = 1; column < headers.Count; column++)
        {
            var header = headers[column];
            var coordinates = CoordinatesPattern().Match(Text(header, "planet-coords"));
            if (!coordinates.Success)
                continue; // the "Sum" column

            var fields = FieldsPattern().Match(Text(header, "planet-fields"));
            var temperature = TemperaturePattern().Match(Text(header, "planet-temperature"));
            var headerValue = Number(Text(header, "planet-energy-value"));

            var resourceRows = 0;
            var resources = ResourceSet.Zero;
            var production = ResourceSet.Zero;
            var storage = ResourceSet.Zero;
            var buildings = new Dictionary<BuildingType, int>();
            var ships = new Dictionary<string, long>(StringComparer.Ordinal);
            var defenses = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (kind, ids, sectionColumns) in sections)
            {
                var values = Values(sectionColumns, column, ids.Count);
                switch (kind)
                {
                    case "resource":
                        var set = new ResourceSet(values.ElementAtOrDefault(0), values.ElementAtOrDefault(1), values.ElementAtOrDefault(2));
                        if (resourceRows++ == 0)
                            resources = set;
                        else
                            production = set;
                        break;
                    case "building" when ids.All(Storages.Contains) && !moons:
                        storage = new ResourceSet(Value(ids, values, "METAL_STORAGE"), Value(ids, values, "CRYSTAL_STORAGE"), Value(ids, values, "DEUTERIUM_TANK"));
                        break;
                    case "building":
                        for (var i = 0; i < ids.Count; i++)
                            if (BuildingCatalog.TryGetByServerId(ids[i], out var building))
                                buildings[building] = (int)values[i];
                        break;
                    case "research" when researches is not null && researches.Count == 0:
                        for (var i = 0; i < ids.Count; i++)
                            researches[ids[i]] = (int)values[i];
                        break;
                    case "defense":
                        for (var i = 0; i < ids.Count; i++)
                            defenses[ids[i]] = values[i];
                        break;
                    case "hangar":
                        for (var i = 0; i < ids.Count; i++)
                            ships[ids[i]] = values[i];
                        break;
                }
            }

            planets.Add(new EmpirePlanet(
                HtmlEntity.DeEntitize(Text(header, "planet-name")),
                new Coordinates(Int(coordinates.Groups[1].Value), Int(coordinates.Groups[2].Value), Int(coordinates.Groups[3].Value)),
                fields.Success ? Int(fields.Groups[1].Value) : 0,
                fields.Success ? Int(fields.Groups[2].Value) : 0,
                temperature.Success ? int.Parse(temperature.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0,
                temperature.Success ? int.Parse(temperature.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0,
                moons ? null : headerValue,
                moons ? (int)headerValue : null,
                resources,
                production,
                storage,
                buildings,
                ships,
                defenses));
        }

        return planets;
    }

    /// <summary>A section's icon folder (its kind), the ids of its values and its columns.</summary>
    private static (string Kind, List<string> Ids, List<HtmlNode> Columns) Section(HtmlNode row)
    {
        var columns = Columns(row);
        var ids = new List<string>();
        var kind = string.Empty;
        if (columns.Count == 0)
            return (kind, ids, columns);
        foreach (var title in BuildingPageParser.ByClass(columns[0], "div", "prop-sub-title"))
        {
            var icon = IconPattern().Match(title.Descendants("img").FirstOrDefault()?.GetAttributeValue("src", string.Empty) ?? string.Empty);
            if (!icon.Success)
                throw new OgamexException("The empire page has a value without an icon.");
            kind = icon.Groups["kind"].Value;
            ids.Add(icon.Groups["id"].Value.ToUpperInvariant());
        }
        return (kind, ids, columns);
    }

    private static List<HtmlNode> Columns(HtmlNode row) =>
        row.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element && n.HasClass("col")).ToList();

    private static List<long> Values(List<HtmlNode> columns, int column, int expected)
    {
        var values = column < columns.Count
            ? BuildingPageParser.ByClass(columns[column], "span", "cell-value").Select(s => Number(s.InnerText)).ToList()
            : [];
        if (values.Count != expected)
            throw new OgamexException($"The empire page has {values.Count} values instead of {expected} in a section of column {column}.");
        return values;
    }

    private static long Value(List<string> ids, List<long> values, string id) => ids.IndexOf(id) is >= 0 and var i ? values[i] : 0;

    private static string Text(HtmlNode column, string cssClass) =>
        BuildingPageParser.ByClass(column, "span", cssClass).FirstOrDefault()?.InnerText.Trim() ?? string.Empty;

    /// <summary>"1.234.567" (dots or commas as thousands separators, a leading minus kept).</summary>
    private static long Number(string text)
    {
        text = HtmlEntity.DeEntitize(text).Trim();
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
            return 0;
        var number = long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return text.StartsWith('-') ? -number : number;
    }

    private static int Int(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[(\d+):(\d+):(\d+)\]")]
    private static partial Regex CoordinatesPattern();

    [GeneratedRegex(@"(\d+)\s*/\s*(\d+)")]
    private static partial Regex FieldsPattern();

    [GeneratedRegex(@"(-?\d+)\s*°C\D*?(-?\d+)\s*°C")]
    private static partial Regex TemperaturePattern();

    [GeneratedRegex(@"/assets/images/(?:V2/(?<kind>building|research|defense|hangar)|(?<kind>resource))/(?<id>[A-Za-z_]+)\.(?:gif|png)")]
    private static partial Regex IconPattern();
}
