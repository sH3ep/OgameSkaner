using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Xml.Serialization;

namespace OgameSkaner.Model
{
    public class GamesConfigurationSerializer

    {
        #region Constants

        private const string ConfigFileName = "GamesConfiguration.xml";

        #endregion
        
        #region MyRegion

        public GamesConfigurationSerializer()
        {
            LoadConfigurationsFromXML();
        }

        #endregion

        #region fields

        private List<GameConfiguration> _gamesConfiguration = new List<GameConfiguration>();

        #endregion

        #region PublicMethods
        

        public GameConfiguration GetConfiguration(GameType gameType, int universum)
        {
            var configuration =
                _gamesConfiguration.FirstOrDefault(x => x.GameType == gameType && x.Universum == universum);
            return configuration;
        }

        public GameConfiguration GetConfiguration(string configurationName)
        {
            var configuration =
                _gamesConfiguration.FirstOrDefault(x => x.ConfigurationName == configurationName);
            return configuration;
        }

        public List<string> GetConfigurationsNames()
        {
            var configurationsNames = new List<string>();
            foreach (var item in _gamesConfiguration)
            {
                configurationsNames.Add(item.ConfigurationName);
            }

            return configurationsNames;
        }

        public async Task AddConfigurationAsync(GameConfiguration gameConfig)
        {
            _gamesConfiguration.RemoveAll((x) =>
                x.GameType == gameConfig.GameType && x.Universum == gameConfig.Universum);
            _gamesConfiguration.Add(gameConfig);
            await SaveConfigurationsIntoXmlAsync();
        }

        public void AddConfiguration(GameConfiguration gameConfig)
        {
            _gamesConfiguration.RemoveAll((x) =>
                x.GameType == gameConfig.GameType && x.Universum == gameConfig.Universum);
            _gamesConfiguration.Add(gameConfig);
            SaveConfigurationsIntoXml();
        }
        
        #endregion

        #region privateMethods
        
        private async Task SaveConfigurationsIntoXmlAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (File.Exists(Path.Combine(ConfigFileName))) File.Delete(Path.Combine(ConfigFileName));

                    var serializer = new XmlSerializer(typeof(List<GameConfiguration>));
                    TextWriter fileStream = new StreamWriter(ConfigFileName);
                    serializer.Serialize(fileStream, _gamesConfiguration);
                    fileStream.Close();
                }
                catch (IOException)
                {
                    MessageBox.Show("There was an error during saving data");
                }
            });
        }

        private void SaveConfigurationsIntoXml()
        {
            try
            {
                if (File.Exists(Path.Combine(ConfigFileName))) File.Delete(Path.Combine(ConfigFileName));

                var serializer = new XmlSerializer(typeof(List<GameConfiguration>));
                TextWriter fileStream = new StreamWriter(ConfigFileName);
                serializer.Serialize(fileStream, _gamesConfiguration);
                fileStream.Close();
            }
            catch (IOException)
            {
                MessageBox.Show("There was an error during saving data");
            }
        }

        private async Task LoadConfigurationsFromXMLAsync()
        {
            await Task.Run(() =>
            {
                if (File.Exists(ConfigFileName))
                {
                    var serializer = new XmlSerializer(typeof(List<GameConfiguration>));
                    TextReader fileStream = new StreamReader(ConfigFileName);
                    _gamesConfiguration = (List<GameConfiguration>)serializer.Deserialize(fileStream);
                    fileStream.Close();
                }
                else
                {
                    MessageBox.Show("Error during reading {0} ", ConfigFileName);
                }
            });
        }

        private void LoadConfigurationsFromXML()
        {

            if (File.Exists(ConfigFileName))
            {
                var serializer = new XmlSerializer(typeof(List<GameConfiguration>));
                TextReader fileStream = new StreamReader(ConfigFileName);
                _gamesConfiguration = (List<GameConfiguration>)serializer.Deserialize(fileStream);
                fileStream.Close();
            }
            else
            {
                MessageBox.Show("Error during reading {0} ", ConfigFileName);
            }
        }
        
        #endregion

    }
}