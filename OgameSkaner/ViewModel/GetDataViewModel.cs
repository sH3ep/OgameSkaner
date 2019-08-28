using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {


        #region fields

        private string _apiRequestCounter;
        private string _token;

        #endregion

        #region Properties

        public string ApiRequestCounter
        {
            set
            {
                _apiRequestCounter = value;
                RaisePropertyChanged("ApiRequestCounter");
            }
            get => _apiRequestCounter;
        }
        public string Token
        {
            set
            {
                _token = value;
                RaisePropertyChanged("ApiRequestCounter");
            }
            get => _token;
        }

        public GalaxySkanRange SkanRange { set; get; }

        public ObservableCollection<UserPlanet> usersPlanets { set; get; }

        public DelegateCommand GetSolarSystemsDataCommand { set; get; }
        #endregion

        #region PrivateMethods

        private async Task GetSolarSystems()
        {
            if (SkanRange.IsValid())
            {
                await Task.Run(async () =>
                {
                    var sGameFileReader = new OgameFileReader();
                    var sGameClient = new SgameRestClient();
                    var dataManager = new UserPlanetDataManager(usersPlanets);
                    string solarSystemPageString;
                    for (var i = SkanRange.StartGalaxy; i <= SkanRange.EndGalaxy; i++)
                        for (var j = SkanRange.StartSystem; j <= SkanRange.EndSystem; j++)
                        {
                            solarSystemPageString = sGameClient.GetSolarSystem(i, j);
                            await sGameFileReader.AddPlayersFromFile(solarSystemPageString, usersPlanets, DateTime.Now);
                            ApiRequestCounter = i.ToString() + ":" + j.ToString();
                        }

                    dataManager.SaveIntoXmlFile("DatabaseFromApi");
                });

            }
            else
            {
                MessageBox.Show("Wrong data");
            }

        }

        private void SaveToken()
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["token"].Value = "6qa24isbmkd4fgj40utvvctbsr";
            config.Save(ConfigurationSaveMode.Modified);
            config.Save();
            ConfigurationManager.RefreshSection("appSettings");
        }

        #endregion

        #region CanExecute
        private bool CanExecute()
        {
            return true;
        }
        #endregion


        public GetDataViewModel()
        {
            usersPlanets = new ObservableCollection<UserPlanet>();
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystems(); });
            var dataManager = new UserPlanetDataManager(usersPlanets);

            if (File.Exists("GalaxyDatabase.xml"))
            {
                usersPlanets = dataManager.LoadFromXml();
            }

            SkanRange = new GalaxySkanRange();
        }






    }
}