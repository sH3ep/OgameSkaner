using System.Security;
using System.Threading.Tasks;
using OgameSkaner.Model;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;

namespace OgameSkaner.RestClient
{

    public interface IGameRestClient
    {
        string GetMainPage();
        string LoginToGame(string login, SecureString password);
        string GetSolarSystem(int galaxy, int solarSystem);
        Task<string> GetSolarSystemAsync(int galaxy, int solarSystem, ProgresBarData pBData);
        LoginStatus CheckLogInStatus();
        void SpyPlanet(UserPlanet userPlanet);
        void SpyPlanet(UserPlanet userPlanet,PlanetType planetType);
        string GetUniversum();
        GameType GetGameType();
    }

}