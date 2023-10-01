using OgameSkaner.Model.Shared;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OgameSkaner.Model.Sgame
{
    public class SgameFileReader: IGameFileReader
    {

        #region Properties

        public DateTime FileCreationDate { set; get; }

        #endregion

        #region PublicMethods

        public async Task AddPlayersFromFile(string fileText, ObservableCollection<UserPlanet> playersPlanets,
            DateTime fileCreationDate, int galaxy, int solarSystem)
        {
            var planetLocalization = "0:0";
            var isGalaxyAndSystemReaded = false;
            var positionLine = "";
            var errorCount = 0;

            var stringReader = new StringReader(fileText);

            while (true)
            {
                _actualLine = stringReader.ReadLine();
                if (_actualLine != null && _actualLine.Contains("System ") && !isGalaxyAndSystemReaded)
                {
                    planetLocalization = await readPlanetLocalization(_actualLine);
                    isGalaxyAndSystemReaded = true;
                    var tempUserPlanet = new UserPlanet("temp", planetLocalization, fileCreationDate);

                    await DeleteOldSolarSystems(tempUserPlanet, playersPlanets);

                }

                if (isGalaxyAndSystemReaded) positionLine = GetLineWithPosition(stringReader, positionLine);

                if (_startToReadUserData)
                {
                    var userName = readUserName(_actualLine);
                    var position = GetPositionFromLine(positionLine);
                    var planetId = GetPlanetId(stringReader);
                    var userPlanet = new UserPlanet(await userName, planetLocalization, position, fileCreationDate);
                    userPlanet.PlanetId = planetId;

                    lock (playersPlanets)
                    {
                        playersPlanets.Add(userPlanet);
                    }

                    _startToReadUserData = false;
                }

                if (_actualLine == null)
                {
                    errorCount++;
                    if (errorCount > 10) break;
                }
            }
        }

        #endregion

        #region PrivateMethods

        private int GetPlanetId(StringReader stringReader)
        {
            int readedLine = 0;
            while (true)
            {
                readedLine++;
                _actualLine = stringReader.ReadLine();
                if (_actualLine != null && (_actualLine.Contains("<a href=\"javascript: doit(") ||
                                            _actualLine.Contains("< a href =\"javascript:doit(") ||
                                            _actualLine.Contains("javascript:doit(")))
                {
                    var isReadingPLanetId = false;
                    var tempPlanetId = "";
                    foreach (var c in _actualLine)
                    {
                        if (c == ',' && isReadingPLanetId)
                        {
                            var planetId = int.Parse(tempPlanetId);
                            return planetId;
                        }


                        if (isReadingPLanetId) tempPlanetId = tempPlanetId + c;


                        if (c == ',' && !isReadingPLanetId) isReadingPLanetId = true;
                    }
                }

                if (readedLine > 10)
                {
                    return 0;
                }
            }
        }

        private string GetLineWithPosition(StringReader stringReader, string previousLine)
        {
            if (_actualLine != null && _actualLine.Contains("<tr>"))
            {
                _actualLine = stringReader.ReadLine();
                if (_actualLine.Contains("<td>"))
                {
                    var position = stringReader.ReadLine();
                    return position;
                }

                if (_actualLine != null && (_actualLine.Contains("<span class=\"galaxy-username") ||
                                            _actualLine.Contains("<span class=\" galaxy-username")))
                {
                    _startToReadUserData = true;
                    return previousLine;
                }
            }

            return previousLine;
        }

        private int GetPositionFromLine(string linePosition)
        {
            try
            {
                var positionString = new string(linePosition.Where(char.IsDigit).ToArray());
                var position = int.Parse(positionString);
                return position;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private Task<string> readPlanetLocalization(string line)
        {
            return Task.Run(() =>
            {
                var isReadingCoordinates = false;
                var coordinates = "";
                foreach (var c in line)
                {
                    if (c == '<' && isReadingCoordinates) return coordinates;


                    if (isReadingCoordinates) coordinates = coordinates + c;

                    if (c == '>' && !isReadingCoordinates) isReadingCoordinates = true;
                }

                return "0:0";
            });
        }

        private Task<string> readUserName(string line)
        {
            return Task.Run(() =>
            {
                var isReadingUserName = false;
                var userName = "";
                foreach (var c in line)
                {
                    if (c == '<' && isReadingUserName) return userName;


                    if (isReadingUserName) userName = userName + c;


                    if (c == '>' && !isReadingUserName) isReadingUserName = true;
                }

                return "0:0";
            });
        }

        private async Task DeleteOldSolarSystems(UserPlanet tempUserPlanet,
            ObservableCollection<UserPlanet> playersPlanets)
        {
            lock (playersPlanets)
            {
                var solarSystemsToRemove = playersPlanets.Where(x =>
                x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem).ToList();

                foreach (var item in solarSystemsToRemove) playersPlanets.Remove(item);
            }

        }

        private DateTime GetSolarSystemCreationDate(UserPlanet tempUserPlanet,
            ObservableCollection<UserPlanet> playersPlanets)
        {
            try
            {
                var solarSystemCreationDate = playersPlanets.First(x =>
                    x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem).CreationDate;
                return solarSystemCreationDate;
            }
            catch (ArgumentNullException)
            {
                return new DateTime(5, 1, 1, 1, 1, 1);
            }
            catch (InvalidOperationException)
            {
                return new DateTime(5, 1, 1, 1, 1, 1);
            }
        }

        #endregion

        #region Fields

        private bool _startToReadUserData;
        private string _actualLine = "";

        #endregion
    }
}