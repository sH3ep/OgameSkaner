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
        private IGameRestClient _restClient =new IWgameRestClient();
        private GameConfiguration _gameConfiguration;

        #endregion

        #region Properties

        public string SelectedGameConfigurationName { set; get; }
        public ObservableCollection<string> GameConfigurationNames { set; get; }

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
            CurrentView = new UserPlanetView();
        }

        private void ShowGetData()
        {
            CurrentView = new GetDataView();

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
           // CreateConfigurationFile();
            LoginRectangleCollor = "red";
            CurrentView = new UserPlanetView();
            ShowGetDataCommand = new DelegateCommand(ShowGetData, CanExecuteButtons);
            ShowUserPlanetViewCommand = new DelegateCommand(ShowUserPlanetView, CanExecuteButtons);
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
            GetAvailableGamesConfigurations();
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