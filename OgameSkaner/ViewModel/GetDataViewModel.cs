using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using OgameSkaner.Model.GameConfiguration;
using OgameSkaner.Model.Shared;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;
using OgameSkaner.View;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {
        public GetDataViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient;
            usersPlanets = new ObservableCollection<UserPlanet>();
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystemAsync(); }, CanExecute);
            LogInCommand = new DelegateCommand(async () => { await LogIn(); }, CanExecute);
            SaveTokenCommand = new DelegateCommand(SaveToken, CanExecute);
            LogOutCommand = new DelegateCommand(LogOut, CanExecute);
            ShowGetTokenHelpCommand = new DelegateCommand(ShowGetTokenHelp);
            ReadSavedLogin();
            PbData = new ProgresBarData();

            var dataManager = new UserPlanetDataManager(usersPlanets);

            usersPlanets = dataManager.LoadFromXml("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());

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
        private IGameRestClient _gameRestClient;

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
                    _gameRestClient.LoginToSgame(Login, SecurePassword);
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
                    var fileReaderFactory = new GameDataReaderFactory();
                    var gameFileReader = fileReaderFactory.CreateFileReader(_gameRestClient.GetGameType());
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
                                solarSysteFile = _gameRestClient.GetSolarSystem(actualGalaxy, actualSolarSystem);
                                await gameFileReader.AddPlayersFromFile(solarSysteFile, usersPlanets, DateTime.Now);
                                ActualPositionReaded = actualGalaxy + ":" + actualSolarSystem;
                            }

                        dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
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
                    var fileReaderFactory = new GameDataReaderFactory();
                    var gameFileReader = fileReaderFactory.CreateFileReader(_gameRestClient.GetGameType());
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
                                try
                                {
                                    getSolarSystemTasks.Add(_gameRestClient.GetSolarSystemAsync(actualGalaxy, actualSolarSystem, PbData));
                                    ActualPositionReaded = actualGalaxy + ":" + actualSolarSystem;
                                }
                                catch (Exception e)
                                {
                                    MessageBox.Show(e.Message + " at positioin " + ActualPositionReaded);
                                }


                            }

                        var solarSystemFiles = await Task.WhenAll(getSolarSystemTasks);
                        PbData.ActualValue = 0;
                        foreach (var item in solarSystemFiles)
                        {
                            await gameFileReader.AddPlayersFromFile(item, usersPlanets, DateTime.Now);
                            PbData.ActualValue++;

                        }

                        dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
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
            var token = new Token(_gameRestClient.GetGameType(), _gameRestClient.GetUniversum());
            token.SaveToken(Token);
            Token = "";
        }

        private void SaveLogin()
        {
            try
            {
                var gameConfigSerializer = new GamesConfigurationSerializer();
                var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameRestClient.GetGameType(),
                        _gameRestClient.GetUniversum());
                gameConfiguration.Login = Login;
                gameConfigSerializer.AddConfiguration(gameConfiguration);
            }
            catch (Exception)
            {
                var logger = new ApplicationLogger(LogFileType.errorLog);
                logger.AddLog("saving login failed");
            }
        }

        private void LogOut()
        {
            var token = new Token(_gameRestClient.GetGameType(), _gameRestClient.GetUniversum());
            token.Delete();
            MessageBox.Show("Logged out");
        }

        private void ReadSavedLogin()
        {
            var gamesConfigSerialiser = new GamesConfigurationSerializer();
            var gameConfiguration =
                gamesConfigSerialiser.GetConfiguration(_gameRestClient.GetGameType(), _gameRestClient.GetUniversum());
            Login = gameConfiguration.Login;
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