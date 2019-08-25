using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Serialization;

namespace OgameSkaner.Model
{
    public class UserPlanetDataManager
    {
        private ObservableCollection<UserPlanet> _userPlanets;

        public UserPlanetDataManager(ObservableCollection<UserPlanet> userPlanets)
        {
            _userPlanets = userPlanets;

        }

        private void DeleteMarkedSolarSystems()
        {
            var tempList = _userPlanets.Where(x => x.ToDelete == false).ToList();
            _userPlanets.Clear();

            foreach (var item in tempList)
            {
                _userPlanets.Add(item);
            }
        }

        public ObservableCollection<UserPlanet> LoadFromXml()
        {
            XmlSerializer serialiser = new XmlSerializer(typeof(ObservableCollection<UserPlanet>));
            TextReader filestream = new StreamReader("GalaxyDatabase.xml");
            _userPlanets.Clear();
            _userPlanets = (ObservableCollection<UserPlanet>)serialiser.Deserialize(filestream);
            filestream.Close();
            return _userPlanets;
        }

        public ObservableCollection<UserPlanet> FilterDataByUserName(string userName)
        {
            ObservableCollection<UserPlanet> filteredList = new ObservableCollection<UserPlanet>();
            if (userName != null && userName.Length > 1)
            {
                var tempList = _userPlanets.Where(x => x.UserName.ToLower().Equals(userName.ToLower()));
                foreach (var item in tempList.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList())
                {
                    filteredList.Add(item);
                }
            }
            else
            {
                foreach (var item in _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList())
                {
                    filteredList.Add(item);
                }
            }

            return filteredList;

        }

        public void SaveIntoXmlFile(string fileName)
        {
            try
            {
                // Check if file exists with its full path    
                if (File.Exists(Path.Combine(fileName + ".xml")))
                {
                    // If file found, delete it    
                    File.Delete(Path.Combine(fileName + ".xml"));

                }

            }
            catch (IOException ioExp)
            {
                MessageBox.Show("Wystapił błąd przy próbie zapisania danych do pliku xml");
            }

            var sortedPlanet = _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList();
            XmlSerializer serialiser = new XmlSerializer(typeof(List<UserPlanet>));
            TextWriter filestream = new StreamWriter("GalaxyDatabase.xml");
            serialiser.Serialize(filestream, sortedPlanet);
            filestream.Close();
        }

        public void SaveIntoTxtFile(string fileName)
        {
            string path = fileName + ".txt";

            var sortedPlanet = _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList();

            // Create a file to write to.
            using (StreamWriter sw = File.CreateText(path))
            {
                string previousSolarSystem = "0:0";
                foreach (var item in sortedPlanet)
                {
                    if (item.Localization != previousSolarSystem)
                    {
                        previousSolarSystem = item.Localization;
                        sw.WriteLine(" ");
                        sw.WriteLine(item.Localization + "  " + item.UserName);
                    }
                    else
                    {
                        sw.WriteLine(item.Localization + "  " + item.UserName);
                    }
                }
                sw.Close();
            }
        }

        //public async Task LoadFromPhpFileOld(string folderLocalization)
        //{
        //    var reader = new OgameFileReader();
        //    var fileList = Directory.EnumerateFiles(folderLocalization, "*.txt");
        //    foreach (string file in fileList)
        //    {
        //        string fileText = File.ReadAllText(file);
        //        await reader.AddPlayersFromFile(fileText, _userPlanets);
        //    }
        //    DeleteMarkedSolarSystems();

        //}

        public async Task LoadFromPhpFile(string folderLocalization)
        {
            var reader = new OgameFileReader();
            DirectoryInfo d = new DirectoryInfo(folderLocalization);//Assuming Test is your Folder
            FileInfo[] files = d.GetFiles("*.txt"); //Getting Text files
            string fileName;
            foreach (FileInfo file in files)
            {
                fileName = file.Name.ToString();
                StreamReader sr = File.OpenText(Path.Combine(folderLocalization, fileName));
                string fileText = File.ReadAllText(Path.Combine(folderLocalization, fileName));
                var fileCreationDate = file.CreationTime;
                await reader.AddPlayersFromFile(fileText, _userPlanets, fileCreationDate);

            }
            DeleteMarkedSolarSystems();
        }

    }
}