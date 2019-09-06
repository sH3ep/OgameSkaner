using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;
using OgameSkaner.View;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {
        public GetDataViewModel()
        {
            usersPlanets = new ObservableCollection<UserPlanet>();
            //GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystems(); }, CanExecute);GetSolarSystemAsync
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystemAsync(); }, CanExecute);
            LogInCommand = new DelegateCommand(async () => { await LogIn(); }, CanExecute);
            SaveTokenCommand = new DelegateCommand(SaveToken, CanExecute);
            LogOutCommand = new DelegateCommand(LogOut, CanExecute);
            ShowGetTokenHelpCommand = new DelegateCommand(ShowGetTokenHelp);
            ReadSavedLogin();
            PbData = new ProgresBarData();

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

        private GameType _gameType = GameType.IWgame;
        private string _actualPositionReaded;
        private string _token;

        #endregion

        #region Properties

        public ProgresBarData PbData { set; get; }

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
                    var sGameClient = new IWgameRestClient();
                    sGameClient.LoginToSgame(Login, SecurePassword);
                    SaveLogin();
                }
                catch (RestException e)
                {
                    MessageBox.Show(e.Message);
                }
            });
        }

        private int CountElementsToDownload()
        {
            var galaxyAmount = SkanRange.EndGalaxy - SkanRange.StartGalaxy + 1;
            var solarSystemAmount = SkanRange.EndSystem - SkanRange.StartSystem + 1;
            return galaxyAmount * solarSystemAmount;
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

        private async Task GetSolarSystemAsync() //todo this need refactor
        {
            if (SkanRange.IsValid())
            {
                PbData.ActualValue = 0;
                PbData.MaxValue = CountElementsToDownload();
                await Task.Run(async () =>
                {
                    var sGameFileReader = new OgameFileReader();
                    var sGameClient = new SgameRestClient();
                    var dataManager = new UserPlanetDataManager(usersPlanets);
                    var getSolarSystemTasks = new List<Task<string>>();
                    try
                    {
                        for (var actualGalaxy = SkanRange.StartGalaxy;
                            actualGalaxy <= SkanRange.EndGalaxy;
                            actualGalaxy++)
                        for (var actualSolarSystem = SkanRange.StartSystem;
                            actualSolarSystem <= SkanRange.EndSystem;
                            actualSolarSystem++)
                        {
                            getSolarSystemTasks.Add(
                                sGameClient.GetSolarSystemAsync(actualGalaxy, actualSolarSystem, PbData));
                            ActualPositionReaded = actualGalaxy + ":" + actualSolarSystem;
                        }

                        var solarSystemFiles = await Task.WhenAll(getSolarSystemTasks);
                        PbData.ActualValue = 0;
                        foreach (var item in solarSystemFiles)
                        {
                            await sGameFileReader.AddPlayersFromFile(item, usersPlanets, DateTime.Now);
                            PbData.ActualValue++;
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
            }
            else
            {
                MessageBox.Show("Wrong data");
            }
        }

        private void SaveToken()
        {
            var token = new Token(_gameType);
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
            var token = new Token(_gameType);
            token.Delete();
            MessageBox.Show("Logged out");
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