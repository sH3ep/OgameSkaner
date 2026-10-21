using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Defense;

namespace OgameSkaner.Core.Ogamex.Parsing;

/// <summary>
/// Parses the event list (<c>/home/fleetmovementlist</c>, the fragment behind the header's "Missions"): one
/// <c>tr</c> per fleet with the mission and the relation in its classes, the time left in <c>data-remaining-seconds</c>,
/// the origin as <c>a.fleet-source-coords</c> after its name, the ship count, and the target's name and coordinates.
/// </summary>
public static partial class FleetMovementParser
{
    private const string MissionClassPrefix = "row-mission-type-";

    /// <param name="now">When the list was read; <c>data-remaining-seconds</c> counts from then.</param>
    /// <exception cref="SessionExpiredException">A whole page came back instead of the fragment (a login redirect).</exception>
    public static IReadOnlyList<FleetMovement> Parse(string? html, DateTimeOffset now)
    {
        html ??= string.Empty;
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase))
            throw new SessionExpiredException("The server did not return the event list. Check the token and session id.");

        var document = new HtmlDocument();
        document.LoadHtml(html);

        var movements = new List<FleetMovement>();
        foreach (var row in document.DocumentNode.Descendants("tr"))
        {
            var classes = row.GetClasses().ToList();
            var missionClass = classes.FirstOrDefault(c => c.StartsWith(MissionClassPrefix, StringComparison.Ordinal));
            if (missionClass is null)
                continue;

            var cells = row.Elements("td").ToList();
            var places = cells
                .Select((cell, index) => (Cell: cell, Index: index, Link: cell.Descendants("a").FirstOrDefault(a => CoordinatesPattern().IsMatch(a.InnerText))))
                .Where(p => p.Link is not null)
                .ToList();
            if (places.Count < 2)
                continue;

            var origin = places.FirstOrDefault(p => p.Link!.HasClass("fleet-source-coords"));
            if (origin.Link is null)
                origin = places[0];
            var target = places.Last(p => p.Index != origin.Index);

            var mission = ParseMission(missionClass[MissionClassPrefix.Length..]);
            var relation = classes.Contains("row-hostile-mission") ? MovementRelation.Hostile
                : classes.Contains("row-friendly-mission") ? MovementRelation.Friendly
                : MovementRelation.Own;
            var remaining = cells.Select(c => c.GetAttributeValue("data-remaining-seconds", null)).FirstOrDefault(v => v is not null);
            var arrivesAt = now + TimeSpan.FromSeconds(long.TryParse(remaining, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? Math.Max(0, seconds) : 0);
            var originCoordinates = ReadCoordinates(origin.Link!);
            var targetCoordinates = ReadCoordinates(target.Link!);
            var ships = origin.Index + 1 < cells.Count ? FleetPageParser.ParseNumber(cells[origin.Index + 1].InnerText) : 0;
            var sender = row.Descendants().FirstOrDefault(n => n.HasClass("fleet-sender-player")) is { } senderNode
                ? Text(senderNode.InnerText)
                : null;
            var returnButton = row.Descendants("a").FirstOrDefault(a => a.HasClass("x_btn_fleet_return"));
            long? returnTimestamp = long.TryParse(returnButton?.GetAttributeValue("data-fleet-start-timestamp", null), NumberStyles.Integer, CultureInfo.InvariantCulture, out var started)
                ? started
                : null;
            var id = row.GetAttributeValue("data-fleet-id", null)
                     ?? $"{mission}:{originCoordinates}>{targetCoordinates}@{arrivesAt.ToUnixTimeSeconds() / 60}";

            movements.Add(new FleetMovement(
                id,
                mission,
                relation,
                classes.Contains("row-fleet-return"),
                arrivesAt,
                originCoordinates,
                PlaceName(origin.Cell, origin.Link!),
                targetCoordinates,
                PlaceName(target.Cell, target.Link!),
                ships,
                string.IsNullOrEmpty(sender) ? null : sender,
                returnTimestamp));
        }

        return movements;
    }

    /// <summary>"ACS_ATTACK" → <see cref="MovementMission.AcsAttack"/>; unknown ids give <see cref="MovementMission.Unknown"/>.</summary>
    internal static MovementMission ParseMission(string serverId) =>
        serverId.All(c => char.IsAsciiLetter(c) || c == '_')
        && Enum.TryParse<MovementMission>(serverId.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var mission)
            ? mission
            : MovementMission.Unknown;

    private static Coordinates ReadCoordinates(HtmlNode link)
    {
        var match = CoordinatesPattern().Match(link.InnerText);
        return new Coordinates(Int(match.Groups[1].Value), Int(match.Groups[2].Value), Int(match.Groups[3].Value));
    }

    /// <summary>The text in front of the coordinates link ("Home planet [8:260:3]").</summary>
    private static string PlaceName(HtmlNode cell, HtmlNode link) =>
        Text(string.Concat(cell.ChildNodes.TakeWhile(n => n != link).Select(n => n.InnerText)));

    private static string Text(string text) => HtmlEntity.DeEntitize(text).Trim();

    private static int Int(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[(\d+):(\d+):(\d+)\]")]
    private static partial Regex CoordinatesPattern();
}
