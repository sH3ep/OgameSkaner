using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {
        private int _apiRequestCounter;

        public GetDataViewModel()
        {usersPlanets = new ObservableCollection<UserPlanet>();
            DeclareDelgateCommand();
            
        }

        public int ApiRequestCounter
        {
            set
            {
                _apiRequestCounter = value;
                RaisePropertyChanged("ApiRequestCounter");
            }
            get => _apiRequestCounter;
        }

        public ObservableCollection<UserPlanet> usersPlanets { set; get; }

        public DelegateCommand GetSolarSystemsDataCommand { set; get; }

        private void DeclareDelgateCommand()
        {
            GetSolarSystemsDataCommand = new DelegateCommand(GetSolarSystems, CanExecute);
            var dataManager = new UserPlanetDataManager(usersPlanets);

            Directory.CreateDirectory(string.Concat((object)Directory.GetCurrentDirectory(), "\\Data"));
            if (File.Exists("GalaxyDatabase.xml"))
            {
                dataManager.LoadFromXml();
              
            }
        }

        private async void GetSolarSystems()
        {
            ApiRequestCounter = 0;
            var sGameFileReader = new OgameFileReader();
            var sGameClient = new SgameRestClient();
            var dataManager = new UserPlanetDataManager(usersPlanets);
            string solarSystemPageString;
            for (var i = 1; i <= 7; i++)
            for (var j = 1; j <= 499; j++)
            {
                solarSystemPageString = sGameClient.GetSolarSystem(i, j);
                await sGameFileReader.AddPlayersFromFile(solarSystemPageString, usersPlanets, DateTime.Now);
                ApiRequestCounter++;
            }

            dataManager.SaveIntoXmlFile("DatabaseFromApi.xml");
        }

        private bool CanExecute()
        {
            return true;
        }


        private void SaveToken()
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["token"].Value = "6qa24isbmkd4fgj40utvvctbsr";
            config.Save(ConfigurationSaveMode.Modified);
            config.Save();
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}