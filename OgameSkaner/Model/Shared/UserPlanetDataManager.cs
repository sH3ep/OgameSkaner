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


        #region Constructors

        public UserPlanetDataManager(ObservableCollection<UserPlanet> userPlanets)
        {
            _userPlanets = userPlanets;
        }

        #endregion

        #region PrivateMethods

        private void DeleteMarkedSolarSystems()
        {
            var tempList = _userPlanets.Where(x => x.ToDelete == false).ToList();
            _userPlanets.Clear();

            foreach (var item in tempList) _userPlanets.Add(item);
        }

        #endregion

        #region fields

        #endregion

        #region PublicMethods

        public ObservableCollection<UserPlanet> LoadFromXml(string fileName = "DatabaseFromApi.xml")
        {
            if (File.Exists(fileName))
            {
                var serializer = new XmlSerializer(typeof(ObservableCollection<UserPlanet>));
                TextReader filestream = new StreamReader(fileName);
                _userPlanets.Clear();
                _userPlanets = (ObservableCollection<UserPlanet>) serializer.Deserialize(filestream);
                filestream.Close();
                return _userPlanets;
            }

            return new ObservableCollection<UserPlanet>();
        }

        public ObservableCollection<UserPlanet> FilterDataByUserName(string userName)
        {
            var filteredList = new ObservableCollection<UserPlanet>();
            if (userName != null && userName.Length > 1)
            {
                var tempList = _userPlanets.Where(x => x.UserName.ToLower().Contains(userName.ToLower()));
                foreach (var item in tempList.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList())
                    filteredList.Add(item);
            }
            else
            {
                foreach (var item in _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem)
                    .ThenBy(x => x.Position).ToList()) filteredList.Add(item);
            }

            return filteredList;
        }

        public void SaveIntoXmlFile(string fileName)
        {
            try
            {
                // Check if file exists with its full path    
                if (File.Exists(Path.Combine(fileName + ".xml"))) File.Delete(Path.Combine(fileName + ".xml"));
            }
            catch (IOException)
            {
                MessageBox.Show("Wystapił błąd przy próbie zapisania danych do pliku xml");
            }

            var sortedPlanet = _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList();
            var serialiser = new XmlSerializer(typeof(List<UserPlanet>));
            TextWriter filestream = new StreamWriter(fileName + ".xml");
            serialiser.Serialize(filestream, sortedPlanet);
            filestream.Close();
        }

        public void SaveIntoTxtFile(string fileName)
        {
            var path = fileName + ".txt";

            var sortedPlanet = _userPlanets.OrderBy(x => x.Galaxy).ThenBy(x => x.SolarSystem).ToList();

            // Create a file to write to.
            using (var sw = File.CreateText(path))
            {
                var previousSolarSystem = "0:0";
                foreach (var item in sortedPlanet)
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

                sw.Close();
            }
        }

        public async Task LoadFromPhpFile(string folderLocalization)
        {
            var reader = new SgameFileReader();
            var d = new DirectoryInfo(folderLocalization); //Assuming Test is your Folder
            var files = d.GetFiles("*.txt"); //Getting Text files
            string fileName;
            foreach (var file in files)
            {
                fileName = file.Name;
                var sr = File.OpenText(Path.Combine(folderLocalization, fileName));
                var fileText = File.ReadAllText(Path.Combine(folderLocalization, fileName));
                var fileCreationDate = file.CreationTime;
                await reader.AddPlayersFromFile(fileText, _userPlanets, fileCreationDate);
            }

            DeleteMarkedSolarSystems();
        }

        #endregion
    }
}