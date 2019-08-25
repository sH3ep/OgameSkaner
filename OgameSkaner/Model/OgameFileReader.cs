using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OgameSkaner.Model
{
    internal class OgameFileReader
    {
        private DateTime _fileCreationDate;

        public DateTime FileCreationDate
        {
            set { _fileCreationDate = value; }
            get { return _fileCreationDate; }
        }

        public async Task AddPlayersFromFile(string fileText, ObservableCollection<UserPlanet> playersPlanets, DateTime creationDate)
        {
            _fileCreationDate = creationDate;
            StringReader stringReader = new StringReader(fileText);
            string line = "";
            string planetLocalization = "0:0";
            bool isLocalizationReaded = false;
            int errorCount = 0;
            while (true)
            {
                line = stringReader.ReadLine();
                if (line != null && line.Contains("System ") && !isLocalizationReaded)
                {
                    planetLocalization = await readPlanetLocalization(line);
                    isLocalizationReaded = true;
                    var tempUserPlanet = new UserPlanet("temp", planetLocalization, creationDate);
                    var solarSystemCreationDate = GetSolarSystemCreationDate(tempUserPlanet, playersPlanets);
                    if (_fileCreationDate > solarSystemCreationDate)
                    {
                        await markToDelete(tempUserPlanet, playersPlanets);
                    }
                    else
                    {
                        break;
                    }


                }


                if (line != null && (line.Contains("<span class=\"galaxy-username") || line.Contains("<span class=\" galaxy-username")) && isLocalizationReaded)
                {
                    var userName = readUserName(line);
                    var userPlanet = new UserPlanet(await userName, planetLocalization, _fileCreationDate);
                    playersPlanets.Add(userPlanet);
                }

                if (line == null)
                {
                    errorCount++;
                    if (errorCount > 100)
                    {
                        break;
                    }
                }

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

        private async Task markToDelete(UserPlanet tempUserPlanet, ObservableCollection<UserPlanet> playersPlanets)
        {
            var solarSystemsToRemove = playersPlanets.Where(x =>
                x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem);

            foreach (var item in solarSystemsToRemove)
            {
                item.ToDelete = true;
            }
        }


        private DateTime GetSolarSystemCreationDate(UserPlanet tempUserPlanet, ObservableCollection<UserPlanet> playersPlanets)
        {
            try
            {
                var solarSystemCreationDate = playersPlanets.First(x =>
                    x.Galaxy == tempUserPlanet.Galaxy && x.SolarSystem == tempUserPlanet.SolarSystem).CreatioDate;
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
    }
}