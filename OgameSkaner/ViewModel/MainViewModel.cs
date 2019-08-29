using System.Drawing;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web.UI;
using System.Windows;
using GalaSoft.MvvmLight;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.View;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class MainViewModel : ViewModelBase
    {

        #region fields
        private System.Windows.Controls.UserControl _currentView;
        private string _loginRectangleCollor;
        private string _loginStatus;
        #endregion

        #region Properties

        public string LoginRectangleCollor
        {
            set
            {
                _loginRectangleCollor = value;
                RaisePropertyChanged("LoginRectangleCollor");
            }
            get { return _loginRectangleCollor; }
        }

        public string LoginStatus { get { return _loginStatus; }
            set { _loginStatus = value;
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
            LoginRectangleCollor = "red";
            CurrentView = new UserPlanetView();
            ShowGetDataCommand = new DelegateCommand(ShowGetData, CanExecuteButtons);
            ShowUserPlanetViewCommand = new DelegateCommand(ShowUserPlanetView, CanExecuteButtons);
            BackgroundPath = Directory.GetCurrentDirectory() + "/Images/bg_sgame.jpg";
            CheckLogInStatus();
        }

        private async Task CheckLogInStatus()
        {
            await Task.Run(async () =>
            {
                try
                {
                    var sGameClient = new SgameRestClient();
                    LoginStatus status = RestClient.LoginStatus.LoggedOut;
                    while (true)
                    {
                        
                        status = sGameClient.CheckLogInStatus();
                        if (status == RestClient.LoginStatus.LoggedIn)
                        {
                            LoginStatus = "LoggedIn";
                            LoginRectangleCollor = "green";
                        }
                        if (status == RestClient.LoginStatus.LoggedOut)
                        {
                            LoginStatus = "LoggedOut";
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