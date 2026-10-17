using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Services;
using OgameSkaner.Core.Tests.Fakes;
using static OgameSkaner.Core.Tests.Fakes.TestData;

namespace OgameSkaner.Core.Tests.Services;

public class GalaxyScanServiceTests
{
    private static readonly UniverseProfile Universe = new("tron");

    private readonly FakePlanetRepository _repository = new();
    private readonly FakeOgamexClient _client = new(Universe);

    private GalaxyScanService CreateService(int saveInterval = 25) =>
        new(_repository, NullLogger<GalaxyScanService>.Instance) { SaveInterval = saveInterval };

    [Fact]
    public async Task Scans_every_system_reports_progress_and_stores_planets()
    {
        _client.SolarSystems = a => [Planet($"P{a.Galaxy}-{a.System}", a.Galaxy, a.System, 4)];
        var reports = new List<ScanProgress>();

        var result = await CreateService().ScanAsync(_client, new ScanRange(1, 1, 2, 3), new SynchronousProgress<ScanProgress>(reports.Add));

        Assert.Equal(new ScanResult(ScanOutcome.Completed, 6, 6), result);
        Assert.Equal(6, _client.RequestedSystems.Count);
        Assert.Equal(Enumerable.Range(1, 6), reports.Select(r => r.Completed));
        Assert.Equal(6, (await _repository.GetAsync("tron")).Count);
    }

    [Fact]
    public async Task Saves_periodically_and_at_the_end()
    {
        await CreateService(saveInterval: 2).ScanAsync(_client, new ScanRange(1, 1, 1, 5));

        // after systems 2 and 4, then the remaining 5th at the end
        Assert.Equal(3, _repository.SaveCount);
    }

    [Fact]
    public async Task Stops_and_saves_when_the_session_expires()
    {
        _client.SolarSystems = a => a.System == 3
            ? throw new SessionExpiredException("expired")
            : [Planet("A", a.Galaxy, a.System, 1)];

        var result = await CreateService().ScanAsync(_client, new ScanRange(1, 1, 1, 10));

        Assert.Equal(ScanOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.SystemsScanned);
        Assert.Equal("expired", result.Error);
        Assert.Equal(1, _repository.SaveCount);
    }

    [Fact]
    public async Task Can_be_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        _client.SolarSystems = a =>
        {
            if (a.System == 2)
                cancellation.Cancel();
            return [];
        };

        var result = await CreateService().ScanAsync(_client, new ScanRange(1, 1, 1, 10), cancellationToken: cancellation.Token);

        Assert.Equal(ScanOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, result.SystemsScanned);
    }

    [Fact]
    public async Task Invalid_range_fails_without_requests()
    {
        var result = await CreateService().ScanAsync(_client, new ScanRange(1, 5, 1, 1));

        Assert.Equal(ScanOutcome.Failed, result.Outcome);
        Assert.Empty(_client.RequestedSystems);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

public class FleetEscapeServiceTests
{
    [Fact]
    public async Task Sends_all_ships_to_the_moon_of_the_current_planet()
    {
        var client = new FakeOgamexClient(new UniverseProfile("tron"))
        {
            Fleet = new FleetSnapshot([new ShipStack("SMALL_CARGO", 10)], new Coordinates(2, 50, 7))
        };

        var result = await new FleetEscapeService().EscapeToMoonAsync(client);

        Assert.True(result.Sent);
        var mission = Assert.Single(client.SentMissions);
        Assert.Equal(new Coordinates(2, 50, 7), mission.Target);
        Assert.Equal(PlanetType.Moon, mission.TargetType);
        Assert.Equal(MissionType.Deploy, mission.Mission);
        Assert.Equal(client.Fleet.Ships, mission.Ships);
    }

    [Fact]
    public async Task Does_nothing_without_ships()
    {
        var client = new FakeOgamexClient(new UniverseProfile("tron"));

        var result = await new FleetEscapeService().EscapeToMoonAsync(client);

        Assert.False(result.Sent);
        Assert.Empty(client.SentMissions);
    }
}

public class PlanetSearchTests
{
    private static readonly PlayerPlanet[] Planets =
    [
        Planet("RAVEN", 2, 1, 1),
        Planet("Raven2", 1, 5, 3),
        Planet("Admin", 1, 1, 1)
    ];

    [Fact]
    public void Empty_query_returns_everything_ordered_by_coordinates()
    {
        Assert.Equal(["Admin", "Raven2", "RAVEN"], PlanetSearch.ByPlayerName(Planets, "  ", false).Select(p => p.PlayerName));
    }

    [Fact]
    public void Contains_search_ignores_case()
    {
        Assert.Equal(["Raven2", "RAVEN"], PlanetSearch.ByPlayerName(Planets, "raven", false).Select(p => p.PlayerName));
    }

    [Fact]
    public void Exact_search_matches_whole_name_ignoring_case()
    {
        Assert.Equal(["RAVEN"], PlanetSearch.ByPlayerName(Planets, " raven ", true).Select(p => p.PlayerName));
    }
}

public class CsvExportServiceTests
{
    [Fact]
    public async Task Writes_header_and_rows_ordered_by_player()
    {
        using var stream = new MemoryStream();

        await new CsvExportService().ExportAsync([Planet("Zed", 1, 2, 3, moon: true), Planet("Amy", 4, 5, 6)], stream);

        var lines = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿').Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "UserName,Galaxy,SolarSystem,Position,Coordinates,HasMoon",
            "Amy,4,5,6,4:5:6,False",
            "Zed,1,2,3,1:2:3,True"
        ], lines);
    }
}
