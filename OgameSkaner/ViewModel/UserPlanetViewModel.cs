using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.Utils;
using OgameSkaner.View;
using OgameSkaner.WpfExtensions;
using Prism.Commands;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetViewModel : NotifyPropertyChanged
    {
        #region Constructor
        public DelegateCommand SetStrictSearchCommand { set; get; }

        public UserPlanetViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient;
            LoadFromXmlFileCommand = new DelegateCommand(SaveDataIntoTxtFile);
            ShowFilteredDataCommand = new DelegateCommand(ShowFilteredData, canExecuteFilter);
            GetFolderLocalizationCommand = new DelegateCommand(GetFolderLocalization);
            AddTenUserPlanetViewsCommand = new DelegateCommand(AddPlanetsOnScrolling);
            SetStrictSearchCommand = new DelegateCommand(SetStrictSearch, () => true);
            _filteredUsersPlanets = new ObservableCollection<UserPlanet>();
            _usersPlanetsDetailsView = new ObservableCollection<UserPlanetDetailedView>();
            _dataManager = new UserPlanetDataManager(PlayersPlanets);
            LoadFromXmlFile();
        }

        #endregion

        #region CanExecute

        private bool canExecuteFilter()
        {
            return true;
        }

        #endregion

        #region private_fields
        private ObservableCollection<UserPlanet> _filteredUsersPlanets;
        private string _filteredName;
        private readonly UserPlanetDataManager _dataManager;
        private string _folderLocalization = string.Concat((object)Directory.GetCurrentDirectory(), "\\Data");
        private ObservableCollection<UserPlanetDetailedView> _usersPlanetsDetailsView;
        private readonly int _maxShowedPlayer = 50; //todo require setter in view
        private IGameRestClient _gameRestClient;
        private bool _isStrictMode;

        #endregion

        #region private methods

        private void RefreshUsersPlanetsDetails()
        {
            UsersPlanetsDetailsView.Clear();
            var playerLoadedCounter = 0;
            foreach (var item in FilteredUsersPlanets)
            {
                UsersPlanetsDetailsView.Add(new UserPlanetDetailedView(item, _gameRestClient));
                playerLoadedCounter++;
                if (playerLoadedCounter > _maxShowedPlayer) break;
            }
        }

        private void AddPlanetsOnScrolling()
        {
            var actualAmountOfViews = UsersPlanetsDetailsView.Count();
            var quantityOfAddedUserPlanet = 0;
            for (var i = actualAmountOfViews; i < FilteredUsersPlanets.Count; i++)
            {
                quantityOfAddedUserPlanet++;
                UsersPlanetsDetailsView.Add(new UserPlanetDetailedView(FilteredUsersPlanets[i], _gameRestClient));
                if (quantityOfAddedUserPlanet > 4) break;
            }
        }

        private void GetFolderLocalization()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _folderLocalization;
                if (dialog.ShowDialog(this.GetIWin32Window()) == System.Windows.Forms.DialogResult.OK)
                    FolderLocalization = dialog.SelectedPath;
            }
        }


        private void SaveDataIntoTxtFile()
        {
            _dataManager.SaveIntoTxtFile("UkladGraczyWGalaktykach");
        }

        private void ShowFilteredData()
        {
            FilteredUsersPlanets = _dataManager.FilterDataByUserName(FilteredName, _isStrictMode);
            RefreshUsersPlanetsDetails();
        }

        private void LoadFromXmlFile()
        {
            Directory.CreateDirectory(string.Concat((object)Directory.GetCurrentDirectory(), "\\Data"));
            string databaseFileName = "Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum();
            if (File.Exists(databaseFileName + ".xml"))
            {
                _dataManager.LoadFromXml(databaseFileName);
                ShowFilteredData();
                RefreshUsersPlanetsDetails();
            }
        }

        #endregion

        #region public properties

        public ObservableCollection<UserPlanet> PlayersPlanets = new ObservableCollection<UserPlanet>();

        public ObservableCollection<UserPlanetDetailedView> UsersPlanetsDetailsView
        {
            get => _usersPlanetsDetailsView;
            set
            {
                _usersPlanetsDetailsView = value;
                RaisePropertyChanged("UsersPlanetsDetailsView");
            }
        }

        public ObservableCollection<UserPlanet> FilteredUsersPlanets
        {
            get => _filteredUsersPlanets;
            set
            {
                _filteredUsersPlanets = value;
                RaisePropertyChanged("FilteredUsersPlanets");
            }
        }

        public string FilteredName
        {
            get => _filteredName;
            set
            {
                _filteredName = value;
                ShowFilteredData();
                RaisePropertyChanged("FilteredName");
            }
        }

        public string FolderLocalization
        {
            get => _folderLocalization;
            set
            {
                _folderLocalization = value;
                RaisePropertyChanged("FolderLocalization");
            }
        }

        #endregion

        #region Commands

        public DelegateCommand LoadFromXmlFileCommand { set; get; }
        public DelegateCommand ShowFilteredDataCommand { set; get; }
        public DelegateCommand GetFolderLocalizationCommand { set; get; }
        public DelegateCommand GetOverviewPageCommand { set; get; }
        public DelegateCommand AddTenUserPlanetViewsCommand { set; get; }

        #endregion

        public bool IsStrictMode
        {
            set
            {
                _isStrictMode = value;
                RaisePropertyChanged("IsStrictMode");
            }
            get => _isStrictMode;
        }

        private void SetStrictSearch()
        {
            IsStrictMode = !IsStrictMode;
        }
    }
}