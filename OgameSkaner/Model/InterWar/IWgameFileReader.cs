using OgameSkaner.Model.Shared;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OgameSkaner.Model
{
    public class IWgameFileReader : IGameFileReader
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

                    DeleteOldSolarSystems(tempUserPlanet, playersPlanets);
                }

                if (isGalaxyAndSystemReaded) positionLine = GetLineWithPosition(stringReader, positionLine);

                if (_startToReadUserData)
                {
                    var userName = readUserName(_actualLine);
                    var position = GetPositionFromLine(positionLine);
                    var userPlanet = new UserPlanet(userName, planetLocalization, position, fileCreationDate)
                    {
                        PlanetId = 1
                    };
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
            var readedLine = 0;
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

                if (readedLine > 10) return 0;
            }
        }

        private string GetLineWithPosition(StringReader stringReader, string previousLine)
        {
            if (_actualLine != null && _actualLine.Contains("\t<td><a href=\"?page=fleet&amp;galaxy="))
            {

                var lineWithPosition = _actualLine;

                _actualLine = stringReader.ReadLine();
                if (_actualLine != null && _actualLine.Length < 500)
                {
                    return lineWithPosition;
                }

                for (int i = 0; i < 6; i++)
                {
                    _actualLine = stringReader.ReadLine();
                    if (_actualLine != null && _actualLine.Contains("jest na miejscu"))
                    {
                        _startToReadUserData = true;
                        return lineWithPosition;
                    }
                }

            }

            return previousLine;
        }

        private int GetPositionFromLine(string linePosition)
        {
            var pattern = "planet=";
            var patternMatchCounter = 0;

            bool isReadingPosition = false;

            var planetPosition = "";
            foreach (var c in linePosition)
            {
                if (!isReadingPosition)
                {
                    if (pattern[patternMatchCounter] == c)
                    {
                        patternMatchCounter++;
                        if (patternMatchCounter == pattern.Length)
                        {
                            isReadingPosition = true;
                        }
                    }
                    else
                    {
                        patternMatchCounter = 0;
                    }
                }
                else
                {
                    if (c == '&')
                    {
                        try
                        {
                            return int.Parse(planetPosition);
                        }
                        catch (Exception)
                        {
                            return 999;
                        }

                    }
                    planetPosition = planetPosition + c;
                }
            }

            return 999;
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

        private string readUserName(string line)
        {

            var pattert = "Gracz ";
            var pattertMatchCounter = 0;

            bool isReadingName = false;

            var userName = "";
            foreach (var c in line)
            {
                if (!isReadingName)
                {
                    if (pattert[pattertMatchCounter] == c)
                    {
                        pattertMatchCounter++;
                        if (pattertMatchCounter == pattert.Length)
                        {
                            isReadingName = true;
                        }
                    }
                    else
                    {
                        pattertMatchCounter = 0;
                    }
                }
                else
                {
                    if (char.IsWhiteSpace(c))
                    {
                        return userName;
                    }
                    userName = userName + c;
                }
            }

            return "Wrong user name";

        }

        private void DeleteOldSolarSystems(UserPlanet tempUserPlanet,
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