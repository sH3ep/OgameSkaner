using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.Model.GameConfiguration;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace OgameSkaner.ViewModel
{
    public class ManageConfigurationViewModel : ViewModelBase
    {
        #region fields

        private GameType _selectedGametype;
        private GameConfigurationModel _selectedGameConfiguration;
        private IEnumerable<GameType> _gameTypes;
        private IEnumerable<GameConfigurationModel> _gamesConfugurations;
        private string _login;
        private int _spyProbeAmount;
        private int _universum;

        #endregion

        #region Properties

        public string Login
        {
            set
            {
                _login = value;
                RaisePropertyChanged("Login");
            }
            get { return _login; }
        }

        public int SpyProbeAmount
        {
            set
            {
                _spyProbeAmount = value;
                RaisePropertyChanged("SpyProbleAmount");
            }
            get { return _spyProbeAmount; }
        }

        public int Universum
        {
            set
            {
                _universum = value;
                RaisePropertyChanged("Universum");
            }
            get { return _universum; }
        }
        public string BackgroundPath { get; private set; }

        public GameConfigurationModel SelectedGameConfuguration
        {
            set
            {
                _selectedGameConfiguration = value;
                RaisePropertyChanged("SelectedGameConfuguration");

            }
            get { return _selectedGameConfiguration; }
        }

        public IEnumerable<GameConfigurationModel> GamesConfugurations
        {
            set
            {
                _gamesConfugurations = value;
                RaisePropertyChanged("GamesConfugurations");
            }
            get { return _gamesConfugurations; }
        }

        public GameType SelectedGameType
        {
            get { return _selectedGametype; }
            set
            {
                _selectedGametype = value;
                RaisePropertyChanged("SelectedGameType");
            }
        }

        public IEnumerable<GameType> GameTypes
        {
            set
            {
                _gameTypes = value;
                RaisePropertyChanged("GameTypes");
            }
            get { return _gameTypes; }
        }

        #endregion

        #region Commands

        public ICommand RefreshGameConfigurationsCommand { set; get; }
        public ICommand AddOrEditConfigurationCommand { set; get; }
        public ICommand DeleteConfigurationCommand { set; get; }

        #endregion

        #region private_methods

        private void RefreshGameConfigurationData()
        {
            Login = _selectedGameConfiguration.Login;
            SpyProbeAmount = _selectedGameConfiguration.SpyProbeAmount;
            Universum = _selectedGameConfiguration.Universum;
            SelectedGameType = _selectedGameConfiguration.GameType;
        }

        private void LoadGamesConfigrations()
        {
            var configSerializer = new GamesConfigurationSerializer();
            GamesConfugurations = configSerializer.GetConfigurations();
            SelectedGameConfuguration = GamesConfugurations.FirstOrDefault();
            GameTypes = Enum.GetValues(typeof(GameType)).Cast<GameType>().ToList();
        }

        private void AddOrEditConfiguration()
        {
            var messageBoxResult = MessageBox.Show("Are you sure you want to add/save the "+ SelectedGameType + " Uniwersum " + Universum + " configuration?",
                "Change configuration", MessageBoxButton.YesNo);

            if (messageBoxResult == MessageBoxResult.Yes)
            {
                var gameConfiguration = new GameConfigurationModel();
                gameConfiguration.GameType = SelectedGameType;
                gameConfiguration.Login = Login;
                gameConfiguration.BaseUri = "whatever";
                gameConfiguration.Universum = Universum;
                gameConfiguration.SpyProbeAmount = SpyProbeAmount;
                gameConfiguration.CurrentPlanet = "1";
                gameConfiguration.Token = "token";

                var configSerializer = new GamesConfigurationSerializer();
                configSerializer.AddConfiguration(gameConfiguration);
                LoadGamesConfigrations();
            }
           
        }

        private void DeleteConfiguration()
        {
            var messageBoxResult = MessageBox.Show("Are you sure you want to delete the" + SelectedGameType + " Uniwersum " + Universum +" configuration?",
                "Change configuration", MessageBoxButton.YesNo);

            if (messageBoxResult == MessageBoxResult.Yes)
            {
                var configSerializer = new GamesConfigurationSerializer();
                configSerializer.DeleteConfiguration(SelectedGameConfuguration);
                LoadGamesConfigrations();
            }
        }

        #endregion

        public ManageConfigurationViewModel()
        {
            CreateCommands();
            LoadGamesConfigrations();
            RefreshGameConfigurationData();
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
        }

        private void CreateCommands()
        {
            RefreshGameConfigurationsCommand = new DelegateCommand(RefreshGameConfigurationData);
            AddOrEditConfigurationCommand = new DelegateCommand(AddOrEditConfiguration);
            DeleteConfigurationCommand = new DelegateCommand(DeleteConfiguration);
        }


    }

}