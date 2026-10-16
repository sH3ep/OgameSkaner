using System.Collections.Generic;
using System.Threading.Tasks;
using OgameSkaner.Model;
using OgameSkaner.RestClient.OgameX;

namespace OgameSkaner.RestClient
{
    public interface IGameRestClient
    {
        string GetSolarSystem(int galaxy, int solarSystem);
        void SpyPlanet(UserPlanet userPlanet, PlanetType planetType);
        string GetUniversum();
        GameType GetGameType();
        Task<GetFleetResponse> GetFleet();
        Task EscapeFleet(int galaxy, int solar, int position, bool toMoon, Dictionary<string, string> ships);
    }
}
