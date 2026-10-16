using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.Model.GameConfiguration;
using OgameSkaner.RestClient;
using OgameSkaner.Utils;
using OgameSkaner.View;
using Prism.Commands;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using GetDataView = OgameSkaner.View.GetDataView;
using UserPlanetView = OgameSkaner.View.UserPlanetView;

namespace OgameScaner.ViewModel
{
    public class MainViewModel : ViewModelBase
    {

        #region fields
        private System.Windows.Controls.UserControl _currentView;
        private GameConfigurationModel _actualGameConfigurationModel;
        private string _selectedGameConfigurationName;
        private ObservableCollection<string> _gameConfigurationNames;
        #endregion

        #region Properties
        public GameConfigurationModel ActualGameConfigurationModel
        {
            set
            {
                _actualGameConfigurationModel = value;
                RaisePropertyChanged("ActualGameConfigurationModel");

            }
            get { return _actualGameConfigurationModel; }
        }
        public string SelectedGameConfigurationName
        {
            set
            {
                _selectedGameConfigurationName = value;
                RaisePropertyChanged("SelectedGameConfigurationName");//todo  add refresh
                UpdateActualGameConfiguration(value);
                CurrentView = null;
            }
            get { return _selectedGameConfigurationName; }
        }

        private void UpdateActualGameConfiguration(string configurationName)
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            ActualGameConfigurationModel = gameConfigSerializer.GetConfiguration(configurationName);
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
        public DelegateCommand ManageConfigurationCommand { set; get; }
        public DelegateCommand ExportDataCommand { set; get; }
        public DelegateCommand ShowUtilitiesViewCommand { set; get; }

        #endregion

        #region private_Methods

        private void ManageConfiguration()
        {
            var manageGameConfigurationView = new ManageConfigurationView();
            manageGameConfigurationView.Closed += ManageGameConfigurationView_Closed;
            manageGameConfigurationView.ShowDialog();
        }

        private void ExportData()
        {
            var exporter = new CsvExporter();

            string databaseFileName = "Database" + _actualGameConfigurationModel.GameType + _actualGameConfigurationModel.Universum;
            if (File.Exists(databaseFileName + ".xml"))
            {
                var dataManager = new UserPlanetDataManager();
                var players = dataManager.LoadFromXml(databaseFileName).ToList();

                var playersForExport = players.Select(player => new UserPlanetForExport(player)).ToList();
                exporter.ExportPlayersToCsv(playersForExport.OrderBy(x => x.UserName));
            }
        }

        private void ManageGameConfigurationView_Closed(object sender, EventArgs e)
        {
            GetAvailableGamesConfigurations();
        }

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
            var gameRestClient = new OgameXRestClient(_actualGameConfigurationModel.Universum);
            CurrentView = new UserPlanetView(gameRestClient);
        }

        private void ShowUtilitiesView()
        {
            var gameRestClient = new OgameXRestClient(_actualGameConfigurationModel.Universum);
            CurrentView = new UtilitiesView(gameRestClient);
        }

        private void ShowGetData()
        {
            var gameRestClient = new OgameXRestClient(_actualGameConfigurationModel.Universum);
            CurrentView = new GetDataView(gameRestClient);

        }

        private GameConfigurationModel GetGameConfiguration(string gameConfigurationName)
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
            DeclareCommand();
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
            GetAvailableGamesConfigurations();
            _actualGameConfigurationModel = GetGameConfiguration(GameConfigurationNames[0]);
            SelectedGameConfigurationName = _actualGameConfigurationModel.ConfigurationName;

        }

        private void DeclareCommand()
        {
            ShowGetDataCommand = new DelegateCommand(ShowGetData, CanExecuteButtons);
            ShowUserPlanetViewCommand = new DelegateCommand(ShowUserPlanetView, CanExecuteButtons);
            ManageConfigurationCommand = new DelegateCommand(ManageConfiguration, CanExecuteButtons);
            ExportDataCommand = new DelegateCommand(ExportData, CanExecuteButtons);
            ShowUtilitiesViewCommand = new DelegateCommand(ShowUtilitiesView, CanExecuteButtons);
        }
    }
}