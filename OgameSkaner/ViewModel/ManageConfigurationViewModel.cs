using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.Model.GameConfiguration;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                RaisePropertyChanged("SelectedGamaType");
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

        #endregion

        #region private_methods

        private void RefreshGamesConfigurations()
        {
            var configSerializer = new GamesConfigurationSerializer();
            GamesConfugurations = configSerializer.GetConfigurations();
            SelectedGameConfuguration = GamesConfugurations.FirstOrDefault();
            Login = SelectedGameConfuguration.Login;
            SpyProbeAmount = SelectedGameConfuguration.SpyProbeAmount;
            Universum = SelectedGameConfuguration.Universum;
            SelectedGameType = SelectedGameConfuguration.GameType;
        }

        #endregion

        public ManageConfigurationViewModel()
        {
            GameTypes = Enum.GetValues(typeof(GameType)).Cast<GameType>().ToList();
            RefreshGamesConfigurations();
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
        }

        private void CreateCommands()
        {
            RefreshGameConfigurationsCommand = new DelegateCommand(RefreshGamesConfigurations); //todo don't work auto refresh on configuration change
        }
        




    }

}