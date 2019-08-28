using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Xml.Serialization;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.WpfExtensions;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetViewModel : NotifyPropertyChanged
    {
        #region private_fields

        private ObservableCollection<UserPlanet> _filteredUsersPlanets;
        private string _adminCode = "";
        private bool _canExecuteLoadFiles;
        private string _filteredName;
        private UserPlanetDataManager _dataManager;
        private string _folderLocalization = string.Concat((object)Directory.GetCurrentDirectory(), "\\Data");

        #endregion
        
        #region Constructor

        public UserPlanetViewModel()
        {
            LoadDataFromFilesCommand = new DelegateCommand(LoadDataFromFiles, canExecuteLoadFiles);
            LoadFromXmlFileCommand = new DelegateCommand(SaveDataIntoTxtFile, canExecuteLoadFiles);
            ShowFilteredDataCommand = new DelegateCommand(ShowFilteredData, canExecuteFilter);
            TurnOnAdminModeCommand = new DelegateCommand(TurnOnAdminMode, canExecuteFilter);
            GetFolderLocalizationCommand = new DelegateCommand(GetFolderLocalization, canExecuteLoadFiles);
            GetOverviewPageCommand = new DelegateCommand(GetOverviewPage,canExecuteFilter);
            _filteredUsersPlanets = new ObservableCollection<UserPlanet>();
            _dataManager = new UserPlanetDataManager(PlayersPlanets);
            LoadFromXmlFile();
        }



        #endregion

        #region CanExecute

        private bool canExecuteLoadFiles()
        {
            return CanExecuteLoadFiles;
        }

        public bool CanExecuteLoadFiles
        {
            get { return _canExecuteLoadFiles; }
            set
            {
                _canExecuteLoadFiles = value;
                LoadDataFromFilesCommand.RaiseCanExecuteChanged();
                GetFolderLocalizationCommand.RaiseCanExecuteChanged();
            }
        }

        private bool canExecuteFilter()
        {
            return true;
        }
        
        #endregion

        #region private methods

        private void GetOverviewPage()
        {
            var sgameClient = new SgameRestClient();
            sgameClient.LoginToSgame();



            var temp = sgameClient.GetMainPage();

            var temp2 = temp;
        }

        private void GetFolderLocalization()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _folderLocalization;
                if (dialog.ShowDialog(this.GetIWin32Window()) == System.Windows.Forms.DialogResult.OK)
                {
                    FolderLocalization = dialog.SelectedPath.ToString();
                }
            }
        }

        private async void LoadDataFromFiles()
        {
            await _dataManager.LoadFromPhpFile(FolderLocalization);
            SaveDataIntoTxtFile();
            SaveDataIntoXmlFile();
            ShowFilteredData();
        }

        private void SaveDataIntoTxtFile()
        {
            _dataManager.SaveIntoTxtFile("UkladGraczyWGalaktykach");

        }

        private void SaveDataIntoXmlFile()
        {
            _dataManager.SaveIntoXmlFile("GalaxyDatabase");
        }

        private void ShowFilteredData()
        {
            FilteredUsersPlanets = _dataManager.FilterDataByUserName(FilteredName);
        }

        private void LoadFromXmlFile()
        {
            Directory.CreateDirectory(string.Concat((object)Directory.GetCurrentDirectory(), "\\Data"));
            if (File.Exists("DatabaseFromApi.xml"))
            {
                _dataManager.LoadFromXml("DatabaseFromApi.xml");
                ShowFilteredData();
            }
        }

        private void TurnOnAdminMode()
        {
            if (AdminCode.Equals("12345"))
            {
                CanExecuteLoadFiles = true;
                AdminCode = "";
            }
            else
            {
                CanExecuteLoadFiles = false;
                AdminCode = "";
            }
        }

        #endregion

        #region public properties

        public ObservableCollection<UserPlanet> PlayersPlanets = new ObservableCollection<UserPlanet>();

        public ObservableCollection<UserPlanet> FilteredUsersPlanets
        {
            get { return _filteredUsersPlanets; }
            set
            {
                _filteredUsersPlanets = value;
                RaisePropertyChanged("FilteredUsersPlanets");
            }
        }

        public string FilteredName
        {
            get { return _filteredName; }
            set
            {
                _filteredName = value;
                RaisePropertyChanged("FilteredName");
            }
        }

        public string FolderLocalization
        {
            get { return _folderLocalization; }
            set
            {
                _folderLocalization = value;
                RaisePropertyChanged("FolderLocalization");
            }
        }

        public string AdminCode
        {
            get { return _adminCode; }
            set
            {
                _adminCode = value;
                RaisePropertyChanged("AdminCode");
            }
        }

        #endregion

        #region Commands

        public DelegateCommand LoadDataFromFilesCommand { set; get; }
        public DelegateCommand LoadFromXmlFileCommand { set; get; }
        public DelegateCommand ShowFilteredDataCommand { set; get; }
        public DelegateCommand TurnOnAdminModeCommand { set; get; }
        public DelegateCommand GetFolderLocalizationCommand { set; get; }
        public DelegateCommand GetOverviewPageCommand { set; get; }

        #endregion
    }
}
