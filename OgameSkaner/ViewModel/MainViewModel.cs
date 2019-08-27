using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Web.UI;
using System.Windows;
using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.View;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class MainViewModel : ViewModelBase
    {

        #region fields
        private System.Windows.Controls.UserControl _currentView;
        #endregion

        #region Properties
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
            CurrentView = new UserPlanetView();
            ShowGetDataCommand = new DelegateCommand(ShowGetData,CanExecuteButtons);
            ShowUserPlanetViewCommand = new DelegateCommand(ShowUserPlanetView,CanExecuteButtons);
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";

        }
    }
}