using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OgameSkaner.Model
{
    public class OgameFileReader
    {

        #region PublicMethods

        public async Task AddPlayersFromFile(string fileText, ObservableCollection<UserPlanet> playersPlanets, DateTime fileCreationDate)
        {
            string planetLocalization = "0:0";
            bool isGalaxyAndSystemReaded = false;
            string positionLine = "";
            int errorCount = 0;
            var tempUserPlanet = new UserPlanet("temp", planetLocalization, fileCreationDate);
            StringReader stringReader = new StringReader(fileText);

            while (true)
            {
                _actualLine = stringReader.ReadLine();
                if (_actualLine != null && _actualLine.Contains("System ") && !isGalaxyAndSystemReaded)
                {
                    planetLocalization = await readPlanetLocalization(_actualLine);
                    isGalaxyAndSystemReaded = true;
                    await DeleteOldSolarSystems(tempUserPlanet, playersPlanets);
                }

                if (isGalaxyAndSystemReaded)
                {
                    positionLine = GetLineWithPosition(stringReader, positionLine);
                }

                if (_startToReadUserData)
                {
                    var userName = readUserName(_actualLine);
                    var position = GetPositionFromLine(positionLine);
                    var userPlanet = new UserPlanet(await userName, planetLocalization, position, fileCreationDate);

                    playersPlanets.Add(userPlanet);
                    _startToReadUserData = false;
                }

                if (_actualLine == null)
                {
                    errorCount++;
                    if (errorCount > 10)
                    {
                        break;
                    }
                }

            }
        }

        #endregion

        #region PrivateMethods

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
                else if (_actualLine != null && (_actualLine.Contains("<span class=\"galaxy-username") || _actualLine.Contains("<span class=\" galaxy-username")))
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
                string positionString = new string(linePosition.Where(char.IsDigit).ToArray());
                int position = int.Parse(positionString);
                return position;

            }
            catch (Exception e)
            {
                return 0;
            }

        }

        private Task<string> readPlanetLocalization(string line)
        {
            return Task.Run(() =>
            {
                bool isReadingCoordinates = false;
                string coordinates = "";
                foreach (char c in line)
                {


                    if (c == '<' && isReadingCoordinates)
                    {
                        return coordinates;
                    }


                    if (isReadingCoordinates)
                    {
                        coordinates = coordinates + c;
                    }

                    if (c == '>' && !isReadingCoordinates)
                    {
                        isReadingCoordinates = true;
                    }
                }

                return "0:0";
            });
        }

        private Task<string> readUserName(string line)
        {
            return Task.Run(() =>
            {
                bool isReadingUserName = false;
                string userName = "";
                foreach (char c in line)
                {


                    if (c == '<' && isReadingUserName)
                    {
                        return userName;
                    }


                    if (isReadingUserName)
                    {
                        userName = userName + c;
                    }


                    if (c == '>' && !isReadingUserName)
                    {
                        isReadingUserName = true;
                    }
                }
                return "0:0";
            });
        }

        private async Task DeleteOldSolarSystems(UserPlanet tempUserPlanet, ObservableCollection<UserPlanet> playersPlanets)
        {
            var solarSystemsToRemove = playersPlanets.Where(x =>
                x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem);

            foreach (var item in solarSystemsToRemove)
            {
                playersPlanets.Remove(item);
            }
        }

        private DateTime GetSolarSystemCreationDate(UserPlanet tempUserPlanet, ObservableCollection<UserPlanet> playersPlanets)
        {
            try
            {
                var solarSystemCreationDate = playersPlanets.First(x =>
                    x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem).CreationDate;
                return solarSystemCreationDate;
            }
            catch (ArgumentNullException e)
            {
                return new DateTime(5, 1, 1, 1, 1, 1);
            }
            catch (InvalidOperationException e)
            {
                return new DateTime(5, 1, 1, 1, 1, 1);
            }
        }

        #endregion

        #region Fields

        private DateTime _fileCreationDate;
        private bool _startToReadUserData = false;
        private string _actualLine = "";

        #endregion

        #region Properties

        public DateTime FileCreationDate
        {
            set { _fileCreationDate = value; }
            get { return _fileCreationDate; }
        }

        #endregion

    }
}