﻿using System.Security;
using System.Threading.Tasks;
using OgameSkaner.Model;
using OgameSkaner.Utils;

namespace OgameSkaner.RestClient
{

    public interface IGameRestClient
    {
        string GetMainPage();
        string LoginToSgame(string login, SecureString password);
        string GetSolarSystem(int galaxy, int solarSystem);
        Task<string> GetSolarSystemAsync(int galaxy, int solarSystem, ProgresBarData pBData);
        LoginStatus CheckLogInStatus();
        void SpyPlanet(UserPlanet userPlanet);
    }

}