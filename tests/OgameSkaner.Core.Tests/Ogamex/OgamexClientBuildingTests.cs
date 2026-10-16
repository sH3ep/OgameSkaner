using System.Net;
using System.Text;
using OgameSkaner.Core.Domain;
using OgameSkaner.Core.Domain.Buildings;
using OgameSkaner.Core.Ogamex;
using OgameSkaner.Core.Storage;

namespace OgameSkaner.Core.Tests.Ogamex;

/// <summary>The build endpoints of <see cref="OgamexClient"/> against canned server answers.</summary>
public class OgamexClientBuildingTests
{
    private static readonly PlanetId PlanetId = new("11111111-1111-1111-1111-111111111111");

    private readonly StubHandler _handler = new();

    private OgamexClient CreateClient()
    {
        var universe = new UniverseProfile("genesis");
        var sessions = new StubSessionStore();
        return new OgamexClient(universe, new HttpClient(_handler) { BaseAddress = universe.BaseUri }, new CookieContainer(), sessions, new OgamexOptions(), TimeProvider.System);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task Planet_list_comes_from_the_sidebar()
    {
        _handler.Respond = _ => Fixture("building_resource.html");

        var snapshot = await CreateClient().GetOwnPlanetsAsync();

        Assert.Equal([new OwnPlanet(PlanetId, "Home planet", new Coordinates(8, 260, 3))], snapshot.Planets);
        Assert.Equal(PlanetId, snapshot.CurrentPlanet);
        Assert.Equal("/building/resource", Assert.Single(_handler.Requests).Uri.PathAndQuery);
    }

    [Fact]
    public async Task Build_state_switches_planet_and_merges_resource_and_facility_pages()
    {
        _handler.Respond = r => Fixture(r.RequestUri!.AbsolutePath.EndsWith("facility", StringComparison.Ordinal) ? "building_facility.html" : "building_resource.html");

        var state = await CreateClient().GetPlanetBuildStateAsync(PlanetId);

        Assert.Equal(
            ["/building/resource?planet=" + PlanetId.Value, "/building/facility?planet=" + PlanetId.Value],
            _handler.Requests.Select(r => r.Uri.PathAndQuery));
        Assert.Equal(24, state.LevelOf(BuildingType.MetalMine));
        Assert.Equal(10, state.LevelOf(BuildingType.RoboticsFactory));
        Assert.Equal(new ResourceSet(40_751_199, 25_585_754, 6_691_970), state.Resources); // from the later facility page
        Assert.Equal(9, state.Queue.Count);
        Assert.Equal(new ResourceSet(1_943_267, 485_817, 0), state.NextLevelCosts[BuildingType.MetalMine]);
        Assert.Equal([BuildingType.NaniteFactory, BuildingType.University, BuildingType.Terraformer], state.Locked.Order());
        // production 13,594.76 − consumption 4,942.17, as the game reports it (bonuses included)
        Assert.Equal(8_652, state.EnergyBalance);
        Assert.Equal("Home planet", state.Planet.Name);
    }

    [Fact]
    public async Task Build_state_reads_only_the_resource_page_without_facilities()
    {
        _handler.Respond = _ => Fixture("building_resource.html");

        var state = await CreateClient().GetPlanetBuildStateAsync(PlanetId, includeFacilities: false);

        Assert.Single(_handler.Requests);
        Assert.Equal(0, state.LevelOf(BuildingType.RoboticsFactory));
    }

    [Fact]
    public async Task Build_state_carries_capacity_fields_and_vacation_mode()
    {
        _handler.Respond = r => Fixture(r.RequestUri!.AbsolutePath.EndsWith("facility", StringComparison.Ordinal) ? "building_facility.html" : "building_resource.html");

        var state = await CreateClient().GetPlanetBuildStateAsync(PlanetId);

        Assert.Equal(new ResourceSet(45_125_000, 35_906_250, 28_375_000), state.StorageCapacity);
        Assert.Equal(246, state.FreeFields); // from the resources page; the facilities page has none
        Assert.False(state.VacationMode);
    }

    [Fact]
    public async Task Build_state_refuses_a_page_without_mines()
    {
        // a moon has no mines; strip the metal mine block to simulate one
        _handler.Respond = _ => Fixture("building_resource.html").Replace("data-building-type=\"METAL_MINE\"", "data-building-type=\"MOON_BASE\"");

        var error = await Assert.ThrowsAsync<OgamexException>(() => CreateClient().GetPlanetBuildStateAsync(PlanetId, includeFacilities: false));
        Assert.Contains("not a planet", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Select_planet_loads_a_page_with_the_planet_parameter_and_checks_it()
    {
        _handler.Respond = _ => Fixture("building_resource.html");

        await CreateClient().SelectPlanetAsync(PlanetId);
        await Assert.ThrowsAsync<OgamexException>(() => CreateClient().SelectPlanetAsync(new PlanetId("00000000-0000-0000-0000-000000000000")));

        Assert.Equal("/building/resource?planet=" + PlanetId.Value, _handler.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task Build_state_fails_when_the_server_returns_another_planet()
    {
        _handler.Respond = _ => Fixture("building_resource.html");

        await Assert.ThrowsAsync<OgamexException>(() => CreateClient().GetPlanetBuildStateAsync(new PlanetId("00000000-0000-0000-0000-000000000000")));
    }

    [Fact]
    public async Task Enqueue_posts_the_body_the_game_sends()
    {
        _handler.Respond = _ => """{"IsSuccess":true,"Message":null,"ValidationErrors":null}""";

        var outcome = await CreateClient().EnqueueBuildingAsync(PlanetId, BuildingType.DeuteriumSynthesizer);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/building/increasebuilding", request.Uri.PathAndQuery);
        Assert.Equal("""{"BuildingType":"DEUTERIUM_REFINERY","__PlanetId":"11111111-1111-1111-1111-111111111111"}""", request.Body);
        Assert.Equal(EnqueueOutcome.Queued, outcome);
    }

    [Theory]
    [InlineData("Construction queue is full.", EnqueueResult.QueueFull)]
    [InlineData("First, the required technologies must be researched.", EnqueueResult.Rejected)]
    public async Task Enqueue_maps_refusals(string message, EnqueueResult expected)
    {
        _handler.Respond = _ => $$"""{"IsSuccess":false,"Message":"{{message}}","ValidationErrors":null}""";

        var outcome = await CreateClient().EnqueueBuildingAsync(PlanetId, BuildingType.NaniteFactory);

        Assert.Equal(new EnqueueOutcome(expected, message), outcome);
    }

    [Fact]
    public async Task Build_state_carries_durations_and_production()
    {
        _handler.Respond = _ => Fixture("building_resource.html");

        var state = await CreateClient().GetPlanetBuildStateAsync(PlanetId, includeFacilities: false);

        Assert.Equal(new TimeSpan(0, 15, 25), state.NextLevelDurations[BuildingType.MetalMine]);
        Assert.Equal(new ResourceSet(141_571_560, 106_870_723, 4_677_763), state.ProductionPerHour);
    }

    [Fact]
    public async Task Shipyard_state_and_ship_orders()
    {
        _handler.Respond = r => r.Method == HttpMethod.Post
            ? """{"IsSuccess":true,"Message":null,"ValidationErrors":null}"""
            : Fixture("hangar.html");
        var client = CreateClient();

        var shipyard = await client.GetShipyardStateAsync(PlanetId);
        var outcome = await client.BuildShipsAsync(PlanetId, "SOLAR_SATELLITE", 12);

        Assert.Equal(300, shipyard.Satellites);
        Assert.Equal(EnqueueOutcome.Queued, outcome);
        Assert.Equal("/hangar?planet=" + PlanetId.Value, _handler.Requests[0].Uri.PathAndQuery);
        Assert.Equal("/hangar/createship", _handler.Requests[1].Uri.PathAndQuery);
        Assert.Equal("""{"ShipType":"SOLAR_SATELLITE","Quantity":12,"__PlanetId":"11111111-1111-1111-1111-111111111111"}""", _handler.Requests[1].Body);
    }

    [Fact]
    public async Task Enqueue_treats_html_as_an_expired_session()
    {
        _handler.Respond = _ => "<html><body>login</body></html>";

        await Assert.ThrowsAsync<SessionExpiredException>(() => CreateClient().EnqueueBuildingAsync(PlanetId, BuildingType.MetalMine));
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string> Respond { get; set; } = _ => string.Empty;

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Respond(request), Encoding.UTF8) };
        }
    }

    private sealed class StubSessionStore : ISessionStore
    {
        public OgamexSession? Get(string universe) => new("token", "session");

        public void Save(string universe, OgamexSession session)
        {
        }

        public void Delete(string universe)
        {
        }
    }
}
