using OgameSkaner.Model.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OgameSkaner.Model.Sgame
{
    public class OgamexFileReader : IGameFileReader
    {

        #region Properties

        public DateTime FileCreationDate { set; get; }

        #endregion

        #region Consts
        private const string PlanetNumberString = "<span class=\"planet-index\">";
        private const string PlayerNameString = "<span style='margin-top:6px;float:left;'>";
        private const string MoonString = "<div class=\"galaxy-col col-moon\">";

        #endregion

        #region PublicMethods

        public async Task AddPlayersFromFile(string fileText, ObservableCollection<UserPlanet> playersPlanets,
            DateTime fileCreationDate, int galaxy, int solarSystem)
        {
            var tempSolarSystemPlanets = new List<UserPlanet>();
            string actualLine;
            var planetPosition = 0;
            var haveMoon = false;
            string playerName = null;

            var stringReader = new StringReader(fileText);
            try
            {

                while (true)
                {
                    actualLine = stringReader.ReadLine();

                    if (actualLine.Contains(PlanetNumberString))
                    {
                        planetPosition = GetPlanetPosition(actualLine);
                    }

                    if (actualLine.Contains(PlayerNameString))
                    {
                        playerName = GetPlayerName(actualLine);
                    }

                    if (actualLine.Contains(MoonString))
                    {
                        var moonNextLine = stringReader.ReadLine();
                        if (moonNextLine.Length > 20)
                        {
                            haveMoon = true;
                        }
                    }

                    if (actualLine.Contains("<div class=\"galaxy-col col-planet-index"))
                    {
                        if (playerName != null && planetPosition != 0)
                        {
                            var playerPlanet = new UserPlanet(playerName, galaxy, solarSystem, planetPosition, haveMoon);
                            tempSolarSystemPlanets.Add(playerPlanet);
                        }

                        playerName = null;
                        planetPosition = 0;
                        haveMoon = false;
                    }

                    if (actualLine.Contains("<span class=\"planet-index\">16</span>"))
                    {
                        break;
                    }
                }

                UpdateSolarSystem(tempSolarSystemPlanets, playersPlanets, galaxy, solarSystem);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
            }

        }

        #endregion

        #region PrivateMethods

        private int GetPlanetPosition(string planetIndexLine)
        {
            var planetPositionIndexStart = planetIndexLine.IndexOf(PlanetNumberString) + PlanetNumberString.Length;
            var planetPositionPreCut = planetIndexLine.Substring(planetPositionIndexStart);
            var planetPositionPreAndPostCut = planetPositionPreCut.Substring(0, planetPositionPreCut.Length - "</span>".Length);
            return int.Parse(planetPositionPreAndPostCut);
        }

        private string GetPlayerName(string line)
        {
            var playerNameStartIndex = line.IndexOf(PlayerNameString) + PlayerNameString.Length;
            var playerNameEndIndex = line.IndexOf("</span>", playerNameStartIndex);
            var playerName = line.Substring(playerNameStartIndex, playerNameEndIndex - playerNameStartIndex);
            return playerName;
        }

        private void UpdateSolarSystem(IEnumerable<UserPlanet> solarSystemPlanets,
            ObservableCollection<UserPlanet> playersPlanets, int galaxy, int solarSystem)
        {
            if (!solarSystemPlanets.Any())
            {
                return;
            }

            lock (playersPlanets)
            {
                var planetsToRemove = playersPlanets
                    .Where(x => x.Galaxy == galaxy && x.SolarSystem == solarSystem).ToList();

                foreach (var planet in planetsToRemove) playersPlanets.Remove(planet);

                playersPlanets.AddRange(solarSystemPlanets);
            }
        }

        #endregion
    }
}