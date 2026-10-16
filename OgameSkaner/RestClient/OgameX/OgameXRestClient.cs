using HtmlAgilityPack;
using OgameSkaner.Model;
using OgameSkaner.RestClient.OgameX;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OgameSkaner.RestClient
{


    public class OgameXRestClient : IGameRestClient
    {
        public OgameXRestClient(string universum)
        {
            _client = new RestSharp.RestClient("https://" + universum + ".ogamex.net");
            _requestConfigurator = new OgamexRequestConfigurator(universum);
            SetSpecialNumber();
            _universum = universum;
        }

        #region fields
        private string _universum;
        private readonly RestSharp.RestClient _client;
        private readonly OgamexRequestConfigurator _requestConfigurator;
        private long _specialNumber;

        #endregion

        #region PublicMethods

        public GameType GetGameType()
        {
            return GameType.OgameX;
        }
        public string GetUniversum()
        {
            return _universum;
        }

        public void SetSpecialNumber()
        {
            var newSpecialNumber = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            if (DateTimeOffset.FromUnixTimeMilliseconds(newSpecialNumber).AddMinutes(-5) > DateTimeOffset.FromUnixTimeMilliseconds(_specialNumber))
            {
                _specialNumber = newSpecialNumber;
            }
        }

        public string GetSolarSystem(int galaxy, int solarSystem)
        {
            SetSpecialNumber();
            var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);
            _specialNumber++;
            request.AddQueryParameter("x", galaxy.ToString());
            request.AddQueryParameter("y", solarSystem.ToString());
            request.AddQueryParameter("_", _specialNumber.ToString());


            var response = _client.Execute(request);

            var solarSystemPage = response.Content;

            return solarSystemPage;
        }

        public void SpyPlanet(UserPlanet userPlanet, PlanetType planetType)
        {
            var body = new SpyPlanetRequest(userPlanet.Galaxy, userPlanet.SolarSystem, userPlanet.Position, planetType == PlanetType.Moon);
            var request = _requestConfigurator.GetSpyReportReqestConfiguration(body);
            var content = _client.Execute(request).Content;
            var response = JsonSerializer.Deserialize<SpyResponse>(content);

            if (!response.IsSuccess) throw new Exception("Spy failed");
        }

        public async Task<GetFleetResponse> GetFleet()
        {
            var request = _requestConfigurator.Configure(RequestType.GetFleet);

            var response = await _client.ExecuteAsync(request);

            var fleetPage = response.Content;

            var webDocument = new HtmlDocument();
            webDocument.LoadHtml(fleetPage);
            var ships = new Dictionary<string, string>();
            var availableShipsNode = webDocument.GetElementbyId("fleet1_content_container");
            var shipItemTooltips = availableShipsNode.ChildNodes.Where(x => x.Attributes.Any(a => a.Name == "class" && a.Value == "ship-item tooltip"));
            var shipNodes = shipItemTooltips.SelectMany(x => x.ChildNodes.Where(y => y.EndNode.Name == "a"));
            ships = shipNodes.Select(x => new KeyValuePair<string, string>(x.Attributes["data-ship-type"].Value, x.Attributes["data-ship-quantity"].Value)).ToDictionary(x => x.Key, x => x.Value);
            var getFleetResponse = new GetFleetResponse()
            {
                Ships = ships,
            };

            getFleetResponse.TargetX = int.Parse(webDocument.GetElementbyId("fleet2_target_x").Attributes["value"].Value);
            getFleetResponse.TargetY = int.Parse(webDocument.GetElementbyId("fleet2_target_y").Attributes["value"].Value);
            getFleetResponse.TargetZ = int.Parse(webDocument.GetElementbyId("fleet2_target_z").Attributes["value"].Value);

            return getFleetResponse;
        }

        public async Task EscapeFleet(int galaxy, int solar, int position, bool toMoon, Dictionary<string, string> ships)
        {
            var request = _requestConfigurator.Configure(RequestType.SendFleet);

            var body = new SendFleetBody(galaxy, solar, position, 5, toMoon ? 2 : 1, ships.Select(x => new ShipBody() { Quantity = int.Parse(x.Value), ShipType = x.Key }));
            request.AddBody(body);
            await _client.ExecuteAsync(request);
        }

        #endregion
    }
}

public class SendFleetBody
{
    public SendFleetBody() { }

    public SendFleetBody(int galaxy, int solar, int position, int missionType, int targetPlanetType, IEnumerable<ShipBody> ships)
    {
        TargetX = galaxy;
        TargetY = solar;
        TargetZ = position;
        MissionType = missionType;
        TargetPlanetType = targetPlanetType;
        Ships = ships;
    }
    public IEnumerable<ShipBody> Ships { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int TargetZ { get; set; }
    public int TargetPlanetType { get; set; }
    public int SpeedPercentage { get; set; } = 100;
    public int Metal { get; set; } = 0;
    public int Crystal { get; set; } = 0;
    public int Deuterium { get; set; } = 0;
    public int MissionType { get; set; }
}

public class ShipBody
{
    public string ShipType { get; set; }
    public int Quantity { get; set; }
}

public class GetFleetResponse
{
    public Dictionary<string, string> Ships { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int TargetZ { get; set; }
}

public class SpyResponse
{
    public bool IsSuccess { get; set; }
}

