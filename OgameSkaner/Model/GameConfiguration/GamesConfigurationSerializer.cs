using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Serialization;

namespace OgameSkaner.Model.GameConfiguration
{
    public class GamesConfigurationSerializer

    {
        #region Constants

        private const string ConfigFileName = "GamesConfiguration.xml";

        #endregion
        
        #region Constructor

        public GamesConfigurationSerializer()
        {
            LoadConfigurationsFromXML();
        }

        #endregion

        #region fields

        private List<Model.GameConfiguration.GameConfigurationModel> _gamesConfiguration = new List<Model.GameConfiguration.GameConfigurationModel>();

        #endregion

        #region PublicMethods

        public void DeleteConfiguration(GameConfigurationModel gameConfiguration)
        {
            _gamesConfiguration = _gamesConfiguration.Where(x =>
                x.GameType != gameConfiguration.GameType || x.Universum != gameConfiguration.Universum).ToList();
            SaveConfigurationsIntoXml();
        }

        public List<GameConfigurationModel> GetConfigurations()
        {
            return _gamesConfiguration.OrderBy(x=>x.ConfigurationName).ToList();
        }
        public Model.GameConfiguration.GameConfigurationModel GetConfiguration(GameType gameType, int universum)
        {
            var configuration =
                _gamesConfiguration.FirstOrDefault(x => x.GameType == gameType && x.Universum == universum);
            return configuration;
        }

        public Model.GameConfiguration.GameConfigurationModel GetConfiguration(string configurationName)
        {
            var configuration =
                _gamesConfiguration.FirstOrDefault(x => x.ConfigurationName == configurationName);
            return configuration;
        }

        public IEnumerable<string> GetConfigurationsNames()
        {
            var configurationsNames = new List<string>();
            foreach (var item in _gamesConfiguration)
            {
                configurationsNames.Add(item.ConfigurationName);
            }

            configurationsNames.Sort();
            return configurationsNames;
        }

        public async Task AddConfigurationAsync(Model.GameConfiguration.GameConfigurationModel gameConfig)
        {

           _gamesConfiguration.RemoveAll((x) =>
                x.GameType == gameConfig.GameType && x.Universum == gameConfig.Universum);
            _gamesConfiguration.Add(gameConfig);
            await SaveConfigurationsIntoXmlAsync();
        }

        public void AddConfiguration(Model.GameConfiguration.GameConfigurationModel gameConfig)
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

                    var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
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

                var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
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
                    var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
                    TextReader fileStream = new StreamReader(ConfigFileName);
                    _gamesConfiguration = (List<Model.GameConfiguration.GameConfigurationModel>)serializer.Deserialize(fileStream);
                    fileStream.Close();
                }
                else
                {
                    CreateDefaultConfiguration();
                    var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
                    TextReader fileStream = new StreamReader(ConfigFileName);
                    _gamesConfiguration = (List<Model.GameConfiguration.GameConfigurationModel>)serializer.Deserialize(fileStream);
                    fileStream.Close();
                }
            });
        }

        private void LoadConfigurationsFromXML()
        {

            if (File.Exists(ConfigFileName))
            {
                var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
                TextReader fileStream = new StreamReader(ConfigFileName);
                _gamesConfiguration = (List<Model.GameConfiguration.GameConfigurationModel>)serializer.Deserialize(fileStream);
                fileStream.Close();
                if (!_gamesConfiguration.Any())
                {
                    CreateDefaultConfiguration();
                    LoadConfigurationsFromXML();
                }
            }
            else
            {
               CreateDefaultConfiguration();
               var serializer = new XmlSerializer(typeof(List<Model.GameConfiguration.GameConfigurationModel>));
               TextReader fileStream = new StreamReader(ConfigFileName);
               _gamesConfiguration = (List<Model.GameConfiguration.GameConfigurationModel>)serializer.Deserialize(fileStream);
               fileStream.Close();
            }
        }

        private void CreateDefaultConfiguration()
        {
            AddConfiguration(new Model.GameConfiguration.GameConfigurationModel() { BaseUri = "test1", CurrentPlanet = "1", GameType = GameType.Sgame, SpyProbeAmount = 1, Universum = 2,Login = "Login",Token="token"});
        }
        
        #endregion

    }
}