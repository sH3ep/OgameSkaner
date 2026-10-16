using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain.Optimizer;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Reads the account's production and speed bonuses from three pages:
/// <list type="bullet">
/// <item><c>/resource</c> (resource settings): per resource the rows "Researches", "Officers", "Academy", "Premium",
/// "Ranking bonus" and "Class bonus" in absolute numbers; dividing by the mine's own row gives the percentages, so
/// no research/officer formula is needed. Rows are recognised by position (after the building rows, which carry a
/// <c>select.factor-select</c>), not by their translated labels.</item>
/// <item><c>/officer</c>: officer levels (<c>data-officer-type</c>) and the "Each level provides … N%" text.</item>
/// <item><c>/academy</c>: selected class and talent levels; the talent id comes from the tooltip's
/// <c>UpgradeTalent('MINER_CONSTRUCTION_SPEED')</c>, the percentage per level from its "+5% …" text.</item>
/// </list>
/// Officers and talents are matched by id and only the number before "%" is read, so the page language doesn't matter.
/// Premiums for construction/hangar/storage are not readable yet (none was active when the page was captured);
/// production and energy premiums are included through the resource page's "Premium" row.
/// </summary>
public static partial class AccountBonusParser
{
    /// <summary>Production increases, class bonus and economy speed from the resource settings page.</summary>
    /// <exception cref="SessionExpiredException">The content is not the resource settings page.</exception>
    public static ResourceSettings ParseResourceSettings(string? html)
    {
        var document = Load(html);
        var table = document.GetElementbyId("resource-settings-content")?.SelectSingleNode(".//table")
                    ?? throw new SessionExpiredException("The server did not return the resource settings page. Check the token and session id.");

        var rows = table.SelectNodes(".//tr")?.Select(r => r.SelectNodes("./td")?.ToList()).Where(c => c is { Count: >= 5 }).Select(c => c!).ToList()
                   ?? [];
        var buildingRows = rows.Where(cells => cells.Any(c => c.SelectSingleNode(".//select[contains(@class,'factor-select')]") is not null)).ToList();
        if (rows.Count == 0 || buildingRows.Count == 0)
            throw new OgamexException("The resource settings table has an unexpected layout.");

        var basic = Numbers(rows[0]);
        var mines = new double[4];
        foreach (var row in buildingRows)
        {
            var type = row.Select(c => c.SelectSingleNode(".//select")?.GetAttributeValue("data-factor-type", "")).FirstOrDefault(t => !string.IsNullOrEmpty(t));
            var values = Numbers(row);
            switch (type)
            {
                case "METAL_MINE": mines[0] = values[0]; break;
                case "CRYSTAL_MINE": mines[1] = values[1]; break;
                case "DEUTERIUM_REFINERY": mines[2] = values[2]; break;
            }
            if (values[3] > 0)
                mines[3] += values[3]; // energy producers: solar plant, fusion reactor, satellites
        }

        // Researches, Officers, Academy, Premium, Ranking bonus, Class bonus follow the last building row.
        var after = rows.IndexOf(buildingRows[^1]) + 1;
        if (rows.Count < after + 6)
            throw new OgamexException("The resource settings table has fewer bonus rows than expected.");
        var bonus = rows.Skip(after).Take(6).Select(Numbers).ToList();

        ProductionBonus Increase(int column) => mines[column] <= 0
            ? ProductionBonus.None
            : new ProductionBonus(bonus[0][column] / mines[column], bonus[1][column] / mines[column], bonus[2][column] / mines[column],
                bonus[3][column] / mines[column], bonus[4][column] / mines[column]);

        // Class bonus: a share of everything before it (basic income included).
        double ClassShare(int column)
        {
            var before = basic[column] + mines[column] + bonus.Take(5).Sum(r => r[column]);
            return before > 0 ? bonus[5][column] / before : double.NaN;
        }

        var classBonus = Enumerable.Range(0, 3).Select(ClassShare).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).First();
        return new ResourceSettings(
            EconomySpeed: basic[0] / 30,
            Metal: Increase(0),
            Crystal: Increase(1),
            Deuterium: Increase(2),
            Energy: Increase(3),
            ClassBonus: Math.Round(classBonus, 4));
    }

    /// <summary>Officer levels and what each level gives.</summary>
    /// <exception cref="SessionExpiredException">The content is not the officers page.</exception>
    public static IReadOnlyList<BonusSource> ParseOfficers(string? html)
    {
        var document = Load(html);
        var officers = document.DocumentNode.SelectNodes("//div[contains(@class,'officer-detail')]")
                       ?? throw new SessionExpiredException("The server did not return the officers page. Check the token and session id.");

        return officers.Select(o =>
        {
            var id = o.GetAttributeValue("data-officer-type", string.Empty);
            var name = HtmlEntity.DeEntitize(o.SelectSingleNode(".//*[contains(@class,'officer-title')]")?.InnerText ?? id).Trim();
            var levelText = o.SelectSingleNode(".//*[contains(@class,'officer-level')]")?.InnerText ?? string.Empty;
            var level = NumberPattern().Match(levelText) is { Success: true } m ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 0;
            var provides = o.SelectSingleNode(".//*[contains(@class,'bottom-section')]")?.InnerText ?? o.InnerText;
            return new BonusSource(id, name, level, Normalize(HtmlEntity.DeEntitize(provides)));
        }).ToList();
    }

    /// <summary>Selected class and academy skills (name, level, what each level gives).</summary>
    /// <exception cref="SessionExpiredException">The content is not the academy page.</exception>
    public static (string ClassName, IReadOnlyList<BonusSource> Skills) ParseAcademy(string? html)
    {
        var document = Load(html);
        var elements = document.DocumentNode.SelectNodes("//div[contains(@class,'academy-element-container')]")
                       ?? throw new SessionExpiredException("The server did not return the academy page. Check the token and session id.");

        var className = HtmlEntity.DeEntitize(document.DocumentNode
            .SelectSingleNode("//div[contains(@class,'class-info-item') and contains(@class,'selectedClass')]//h3")?.InnerText ?? string.Empty).Trim();

        var skills = elements.Select(e =>
        {
            var content = HtmlEntity.DeEntitize(e.GetAttributeValue("data-tooltip-content", string.Empty));
            var tooltip = new HtmlDocument();
            tooltip.LoadHtml(content);
            // "Resource extraction <span>(Level: 11)</span>"; no span = level 0.
            var titleNode = tooltip.DocumentNode.SelectSingleNode("//div[contains(@class,'title')]");
            var levelText = titleNode?.SelectSingleNode("./span")?.InnerText ?? string.Empty;
            var level = NumberPattern().Match(levelText) is { Success: true } m ? int.Parse(m.Value, CultureInfo.InvariantCulture) : 0;
            var title = HtmlEntity.DeEntitize(titleNode?.InnerText ?? string.Empty);
            var name = Normalize(levelText.Length > 0 ? title.Replace(levelText, string.Empty, StringComparison.Ordinal) : title);
            var provides = Normalize(HtmlEntity.DeEntitize(tooltip.DocumentNode.SelectSingleNode("//div[contains(@class,'provides')]")?.InnerText ?? string.Empty));
            var id = TalentPattern().Match(content) is { Success: true } talent ? talent.Groups[1].Value : name;
            return new BonusSource(id, name, level, provides);
        }).ToList();
        return (className, skills);
    }

    /// <summary>Puts the three pages together.</summary>
    public static AccountBonuses Combine(ResourceSettings production, IReadOnlyList<BonusSource> officers, string className, IReadOnlyList<BonusSource> academy, DateTimeOffset readAt)
    {
        var sources = officers.Concat(academy).Where(s => s.Level > 0).ToList();
        double Sum(Func<BonusSource, double> perLevel) => sources.Sum(s => s.Level * perLevel(s));

        var details = string.Join(", ", sources.Select(s => $"{s.Name} {s.Level}"));
        return new AccountBonuses
        {
            EconomySpeed = production.EconomySpeed,
            Metal = production.Metal,
            Crystal = production.Crystal,
            Deuterium = production.Deuterium,
            Energy = production.Energy,
            ClassBonus = production.ClassBonus,
            ClassName = className,
            ConstructionSpeed = Sum(s => s.Is("CONSTRUCTION_EXPERT", "_CONSTRUCTION_SPEED") ? s.PercentPerLevel : 0),
            ShipyardSpeed = Sum(s => s.Is("TECHNOCRAT", "_HANGAR_SPEED") ? s.PercentPerLevel : 0),
            StorageIncrease = Sum(s => s.Is("STORAGE_EXPERT", "_STORAGE_CAPACITY") ? s.PercentPerLevel : 0),
            Details = details,
            ReadAt = readAt
        };
    }

    private static HtmlDocument Load(string? html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        return document;
    }

    /// <summary>Numbers of the metal, crystal, deuterium and energy columns ("3.096.687", "-449").</summary>
    private static double[] Numbers(List<HtmlNode> cells) =>
        cells.Skip(1).Take(4).Select(c =>
        {
            var text = HtmlEntity.DeEntitize(c.InnerText).Trim().Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
            return double.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }).ToArray();

    private static string Normalize(string text) => WhitespacePattern().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"UpgradeTalent\('([A-Z_]+)'")]
    private static partial Regex TalentPattern();
}

/// <summary>What the resource settings page says about production bonuses.</summary>
public sealed record ResourceSettings(
    double EconomySpeed,
    ProductionBonus Metal,
    ProductionBonus Crystal,
    ProductionBonus Deuterium,
    ProductionBonus Energy,
    double ClassBonus);

/// <summary>An officer or academy talent: its id, level and the text of what each level provides.</summary>
public sealed partial record BonusSource(string Id, string Name, int Level, string Provides)
{
    /// <summary>The first "N%" of what each level provides, as a fraction (Construction expert: 0.1).</summary>
    public double PercentPerLevel => PercentPattern().Match(Provides) is { Success: true } m
        ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100
        : 0;

    /// <summary>True for the officer <paramref name="officer"/> or a talent whose id ends with <paramref name="talentSuffix"/>.</summary>
    public bool Is(string officer, string talentSuffix) =>
        Id == officer || Id.EndsWith(talentSuffix, StringComparison.Ordinal);

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%")]
    private static partial Regex PercentPattern();
}
