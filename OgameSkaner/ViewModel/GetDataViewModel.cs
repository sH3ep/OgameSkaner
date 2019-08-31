using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.View;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {
        public GetDataViewModel()
        {
            usersPlanets = new ObservableCollection<UserPlanet>();
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystems(); }, CanExecute);
            LogInCommand = new DelegateCommand(async () => { await LogIn(); }, CanExecute);
            SaveTokenCommand = new DelegateCommand(SaveToken, CanExecute);
            LogOutCommand = new DelegateCommand(LogOut, CanExecute);
            ShowGetTokenHelpCommand = new DelegateCommand(ShowGetTokenHelp);
            ReadSavedLogin();

            var dataManager = new UserPlanetDataManager(usersPlanets);

            usersPlanets = dataManager.LoadFromXml();

            SkanRange = new GalaxySkanRange();
        }

        #region CanExecute

        private bool CanExecute()
        {
            return true;
        }

        #endregion

        #region fields

        private string _actualPositionReaded;
        private string _token;

        #endregion

        #region Properties

        public SecureString SecurePassword { private get; set; }

        public string Login { set; get; }

        public string ActualPositionReaded
        {
            set
            {
                _actualPositionReaded = value;
                RaisePropertyChanged("ActualPositionReaded");
            }
            get => _actualPositionReaded;
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
                    sGameClient.LoginToSgame(Login, SecurePassword);
                    SaveLogin();
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
                await Task.Run(async () =>
                {
                    var sGameFileReader = new OgameFileReader();
                    var sGameClient = new SgameRestClient();
                    var dataManager = new UserPlanetDataManager(usersPlanets);
                    string solarSysteFile;
                    try
                    {
                        for (var actualGalaxy = SkanRange.StartGalaxy;
                            actualGalaxy <= SkanRange.EndGalaxy;
                            actualGalaxy++)
                        for (var actualSolarSystem = SkanRange.StartSystem;
                            actualSolarSystem <= SkanRange.EndSystem;
                            actualSolarSystem++)
                        {
                            solarSysteFile = sGameClient.GetSolarSystem(actualGalaxy, actualSolarSystem);
                            await sGameFileReader.AddPlayersFromFile(solarSysteFile, usersPlanets, DateTime.Now);
                            ActualPositionReaded = actualGalaxy + ":" + actualSolarSystem;
                        }

                        dataManager.SaveIntoXmlFile("DatabaseFromApi");
                        MessageBox.Show("Saving data finished");
                    }
                    catch (RestException e)
                    {
                        MessageBox.Show(e.Message);
                    }
                    catch (Exception)
                    {
                        MessageBox.Show("Unknow exception, contact with developer");
                    }
                });
            else
                MessageBox.Show("Wrong data");
        }

        private void SaveToken()
        {
            var token = new Token();
            token.SaveToken(Token);
            Token = "";
        }

        private void SaveLogin()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                config.AppSettings.Settings["SgameLogin"].Value = Login;
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception)
            {
               var logger = new ApplicationLogger(LogFileType.errorLog);
               logger.AddLog("saving login failed");
            }

        }

        private void LogOut()
        {
            var token = new Token();
            token.Delete();
        }

        private void ReadSavedLogin()
        {
            Login = ConfigurationManager.AppSettings.Get("SgameLogin");
        }

        private void ShowGetTokenHelp()
        {
            var gifWindow = new GetTokenGifView();
            gifWindow.Show();
        }

        #endregion

        #region Commands

        public DelegateCommand GetSolarSystemsDataCommand { set; get; }

        public DelegateCommand LogInCommand { set; get; }

        public DelegateCommand SaveTokenCommand { set; get; }

        public DelegateCommand LogOutCommand { set; get; }

        public DelegateCommand ShowGetTokenHelpCommand { set; get; }

        #endregion
    }
}