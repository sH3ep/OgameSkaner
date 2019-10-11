using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web.UI;
using System.Windows;
using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.RestClient.Shared;
using Prism.Commands;
using GetDataView = OgameSkaner.View.GetDataView;
using UserPlanetView = OgameSkaner.View.UserPlanetView;

namespace OgameScaner.ViewModel
{
    public class MainViewModel : ViewModelBase
    {

        private void CreateConfigurationFile()
        {
            GamesConfigurationSerializer temp = new GamesConfigurationSerializer();
            temp.AddConfiguration(new GameConfiguration() { BaseUri = "test", CurrentPlanet = "1", GameType = GameType.IWgame, SpyProbeAmount = 1, Universum = 1 });
            temp.AddConfiguration(new GameConfiguration() { BaseUri = "test1", CurrentPlanet = "1", GameType = GameType.Sgame, SpyProbeAmount = 1, Universum = 2 });


        }

        #region fields
        private System.Windows.Controls.UserControl _currentView;
        private string _loginRectangleCollor;
        private string _loginStatus;
        private IGameRestClient _restClient;
        private GameConfiguration _actualGameConfiguration;
        private string _selectedGameConfigurationName;
        private ObservableCollection<string> _gameConfigurationNames;
        #endregion

        #region Properties
        public GameConfiguration ActualGameConfiguration
        {
            set
            {
                _actualGameConfiguration = value;
                RaisePropertyChanged("ActualGameConfiguration");
                
            }
            get { return _actualGameConfiguration; }
        }
        public string SelectedGameConfigurationName
        {
            set
            {
                _selectedGameConfigurationName = value;
                RaisePropertyChanged("SelectedGameConfigurationName");//todo  add refresh
                UpdateActualGameConfiguration(value);
            }
            get { return _selectedGameConfigurationName; }
        }

        private void UpdateActualGameConfiguration(string configurationName)
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            ActualGameConfiguration = gameConfigSerializer.GetConfiguration(configurationName);
        }

        public ObservableCollection<string> GameConfigurationNames
        {
            set
            {
                _gameConfigurationNames = value;
                RaisePropertyChanged("GameConfigurationNames");
            }
            get { return _gameConfigurationNames; }
        }

        public string LoginRectangleCollor
        {
            set
            {
                _loginRectangleCollor = value;
                RaisePropertyChanged("LoginRectangleCollor");
            }
            get { return _loginRectangleCollor; }
        }

        public string LoginStatus
        {
            get { return _loginStatus; }
            set
            {
                _loginStatus = value;
                RaisePropertyChanged("LoginStatus");
            }
        }

        public System.Windows.Controls.UserControl CurrentView
        {
            get { return _currentView; }
            set
            {
                _currentView = value;
                RaisePropertyChanged("CurrentView");
            }
        }

        public string BackgroundPath { set; get; }

        #endregion

        #region Commands

        public DelegateCommand ShowUserPlanetViewCommand { set; get; }
        public DelegateCommand ShowGetDataCommand { set; get; }

        #endregion

        #region private_Methods

        private void GetAvailableGamesConfigurations()
        {
            var configSerializer = new GamesConfigurationSerializer();
            try
            {
                GameConfigurationNames = new ObservableCollection<string>(configSerializer.GetConfigurationsNames());
            }
            catch (Exception e)
            {
                MessageBox.Show("error during loading configuration");
            }
            
          
        }
        private void ShowUserPlanetView()
        {
            var restClientFactory = new GameRestClientFactory();
            var gameRestClient = restClientFactory.CreateRestClient(_actualGameConfiguration.GameType, _actualGameConfiguration.Universum);
            CurrentView = new UserPlanetView(gameRestClient);
        }

        private void ShowGetData()
        {
            var restClientFactory = new GameRestClientFactory();
            var gameRestClient = restClientFactory.CreateRestClient(_actualGameConfiguration.GameType, _actualGameConfiguration.Universum);
            CurrentView = new GetDataView(gameRestClient);

        }

        private GameConfiguration GetGameConfiguration(string gameConfigurationName)
        {
            var serializer = new GamesConfigurationSerializer();
            return serializer.GetConfiguration(gameConfigurationName);
        }

        #endregion

        #region Can_execute

        private bool CanExecuteButtons()
        {
            return true;
        }

        #endregion

        public MainViewModel()
        {
           //CreateConfigurationFile();
            LoginRectangleCollor = "red";
            ShowGetDataCommand = new DelegateCommand(ShowGetData, CanExecuteButtons);
            ShowUserPlanetViewCommand = new DelegateCommand(ShowUserPlanetView, CanExecuteButtons);
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
            GetAvailableGamesConfigurations();
            _actualGameConfiguration = GetGameConfiguration(GameConfigurationNames[0]);
            SelectedGameConfigurationName = _actualGameConfiguration.ConfigurationName;
            CheckLogInStatus();
        }

        private async Task CheckLogInStatus()
        {
            await Task.Run(async () =>
            {
                try
                {
                    
                    LoginStatus status = OgameSkaner.RestClient.LoginStatus.LoggedOut;
                    while (true)
                    {

                        status = _restClient.CheckLogInStatus();
                        if (status == OgameSkaner.RestClient.LoginStatus.LoggedIn)
                        {
                            LoginStatus = "Logged In";
                            LoginRectangleCollor = "green";
                        }
                        if (status == OgameSkaner.RestClient.LoginStatus.LoggedOut)
                        {
                            LoginStatus = "Logged Out";
                            LoginRectangleCollor = "red";
                        }

                        await Task.Delay(10000);

                    }

                }
                catch (RestException e)
                {
                    MessageBox.Show(e.Message);
                }

            });
        }
    }
}