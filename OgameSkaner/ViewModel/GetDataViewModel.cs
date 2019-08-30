using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Security;
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
        private string _login;

        #endregion

        #region Properties

        public SecureString SecurePassword { private get; set; }

        public string Login
        {
            set { _login = value; }
            get { return _login; }
        }

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
                RaisePropertyChanged("Token");
            }
            get => _token;
        }

        public GalaxySkanRange SkanRange { set; get; }

        public ObservableCollection<UserPlanet> usersPlanets { set; get; }


        #endregion

        #region PrivateMethods

        private async Task LogIn()
        {
            await Task.Run(() =>
            {
                try
                {
                    var sGameClient = new SgameRestClient();
                    sGameClient.LoginToSgame(_login, SecurePassword);
                }
                catch (RestException e)
                {
                    MessageBox.Show(e.Message);
                }

            });
        }

        private async Task GetSolarSystems()
        {
            if (SkanRange.IsValid())
            {
                await Task.Run(async () =>
                {
                    var sGameFileReader = new OgameFileReader();
                    var sGameClient = new SgameRestClient();
                    var dataManager = new UserPlanetDataManager(usersPlanets);
                    string solarSysteFile;
                    try
                    {
                        for (var i = SkanRange.StartGalaxy; i <= SkanRange.EndGalaxy; i++)
                            for (var j = SkanRange.StartSystem; j <= SkanRange.EndSystem; j++)
                            {
                                solarSysteFile = sGameClient.GetSolarSystem(i, j);
                                await sGameFileReader.AddPlayersFromFile(solarSysteFile, usersPlanets, DateTime.Now);
                                ApiRequestCounter = i.ToString() + ":" + j.ToString();
                            }

                    }
                    catch (RestException e)
                    {
                        MessageBox.Show(e.Message);

                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Unknow exception, contact with developer");
                    }

                    dataManager.SaveIntoXmlFile("DatabaseFromApi");
                    MessageBox.Show("Saving data finished");
                });

            }
            else
            {
                MessageBox.Show("Wrong data");
            }

        }

        private void SaveToken()
        {
            var token = new Token();
            token.SaveToken(Token);
            Token = "";
        }

        private void LogOut()
        {
            var token = new Token();
            token.Delete();
        }
        #endregion

        #region CanExecute
        private bool CanExecute()
        {
            return true;
        }
        #endregion

        #region Commands

        public DelegateCommand GetSolarSystemsDataCommand { set; get; }

        public DelegateCommand LogInCommand { set; get; }

        public DelegateCommand SaveTokenCommand { set; get; }

        public DelegateCommand LogOutCommand { set; get; }

        #endregion

        public GetDataViewModel()
        {
            usersPlanets = new ObservableCollection<UserPlanet>();
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystems(); },CanExecute);
            LogInCommand = new DelegateCommand(async () => { await LogIn(); },CanExecute);
            SaveTokenCommand = new DelegateCommand(SaveToken, CanExecute);
            LogOutCommand = new DelegateCommand(LogOut, CanExecute);

            var dataManager = new UserPlanetDataManager(usersPlanets);

            if (File.Exists("GalaxyDatabase.xml"))
            {
                usersPlanets = dataManager.LoadFromXml();
            }

            SkanRange = new GalaxySkanRange();
        }






    }
}