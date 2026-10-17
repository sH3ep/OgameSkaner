using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Domain.Raiding;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Ogamex.Parsing;

namespace OgameSkaner.Core.Tests.Ogamex;

public class EspionageReportParserTests
{
    private static readonly TimeSpan ServerOffset = TimeSpan.FromHours(2);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void List_reads_target_time_player_resources_and_counter_espionage()
    {
        var reports = EspionageReportParser.ParseList(Fixture("espionage_list.html"), ServerOffset);

        Assert.Equal(2, reports.Count); // the spy attempt message is not a report
        var report = reports[0];
        Assert.Equal("bbbbbbbb-0000-0000-0000-000000000002", report.Id);
        Assert.Equal(new Coordinates(5, 288, 3), report.Target);
        Assert.Equal(PlanetType.Planet, report.TargetType);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 22, 10, 41, ServerOffset), report.Time);
        Assert.Equal("Kowalski", report.PlayerName);
        Assert.Equal("cccccccc-0000-0000-0000-000000000002", report.PlayerId);
        Assert.Equal(PlayerStatus.LongInactive, report.PlayerStatus);
        Assert.Equal(new ResourceSet(4_250_000_000, 1_100_000_000, 650_000_000), report.Resources);
        Assert.Equal(3, report.CounterEspionagePercent);
        Assert.Null(report.Ships);
        Assert.Null(report.Defenses);
    }

    [Fact]
    public void List_takes_the_target_type_from_the_attack_link()
    {
        var report = EspionageReportParser.ParseList(Fixture("espionage_list.html"), ServerOffset)[1];

        Assert.Equal(PlanetType.Moon, report.TargetType);
        Assert.Equal(PlayerStatus.Inactive, report.PlayerStatus);
        Assert.Equal(100, report.CounterEspionagePercent);
        Assert.Equal(131_233_495_045, report.Resources.Metal);
    }

    [Fact]
    public void Empty_list_has_no_reports()
    {
        const string html = """
            <div>No messages found.</div>
            <script>$('#messages-container .btn-msg-delete').click(function () { });</script>
            """;

        Assert.Empty(EspionageReportParser.ParseList(html, ServerOffset));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<html><body><form id=\"login\"></form></body></html>")]
    public void List_throws_session_expired_for_other_content(string? html)
    {
        Assert.Throws<SessionExpiredException>(() => EspionageReportParser.ParseList(html, ServerOffset));
    }

    [Fact]
    public void Detail_reads_section_points_and_units()
    {
        var (ships, defenses) = EspionageReportParser.ParseDetail(Fixture("spy_report_detail.html"));

        Assert.NotNull(ships);
        Assert.Equal(0, ships.Points);
        Assert.Empty(ships.Units);
        Assert.NotNull(defenses);
        Assert.Equal(2_000, defenses.Points);
        Assert.Equal([new UnitCount("MISSILE_LAUNCHER", 1_000)], defenses.Units);
    }

    [Fact]
    public void Detail_returns_null_for_a_section_the_probes_did_not_see()
    {
        var (ships, defenses) = EspionageReportParser.ParseDetail(Fixture("spy_report_detail_hidden.html"));

        Assert.Equal(0, ships?.Points);
        Assert.Null(defenses);
    }

    [Fact]
    public void Detail_identifies_units_by_english_name_without_an_image()
    {
        const string html = """
            <div id="spy-report-modal">
            <div class="expand-item"><div class="header">Resources</div><div class="content"></div></div>
            <div class="expand-item"><div class="header">Ships (<span>4.000</span>)</div>
            <div class="content"><div><div><span>Light Fighter</span><span>1.000</span></div><div><span>Mystery Ship</span><span>2</span></div></div></div></div>
            </div>
            """;

        var (ships, defenses) = EspionageReportParser.ParseDetail(html);

        Assert.Equal([new UnitCount("LIGHT_FIGHTER", 1_000), new UnitCount("Mystery Ship", 2)], ships!.Units);
        Assert.Null(defenses);
    }

    [Fact]
    public void Detail_throws_session_expired_for_other_content()
    {
        Assert.Throws<SessionExpiredException>(() => EspionageReportParser.ParseDetail("<html><body>Login</body></html>"));
    }
}
