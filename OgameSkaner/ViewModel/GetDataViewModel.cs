using OgameSkaner.Model;
using OgameSkaner.Model.OgameX;
using OgameSkaner.Model.Shared;
using OgameSkaner.RestClient;
using OgameSkaner.Utils;
using OgameSkaner.View;
using Prism.Commands;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace OgameSkaner.ViewModel
{
    public class GetDataViewModel : NotifyPropertyChanged
    {
        public GetDataViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient;
            usersPlanets = new ObservableCollection<UserPlanet>();
            GetSolarSystemsDataCommand = new DelegateCommand(async () => { await GetSolarSystems(); }, CanExecute);
            SaveTokenCommand = new DelegateCommand(SaveTokenAndSessionId, CanExecute);
            ShowGetTokenHelpCommand = new DelegateCommand(ShowGetTokenHelp);
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
        private string _sessionId;
        private IGameRestClient _gameRestClient;

        #endregion

        #region Properties

        public ProgresBarData PbData { set; get; }

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

        public string SessionId
        {
            set
            {
                _sessionId = value;
                RaisePropertyChanged("SessionId");
            }
            get => _sessionId;
        }

        public GalaxySkanRange SkanRange { set; get; }

        public ObservableCollection<UserPlanet> usersPlanets { set; get; }

        #endregion

        #region PrivateMethods

        private int CountElementsToDownload()
        {
            var galaxyAmount = SkanRange.EndGalaxy - SkanRange.StartGalaxy + 1;
            var solarSystemAmount = SkanRange.EndSystem - SkanRange.StartSystem + 1;
            return galaxyAmount * solarSystemAmount;
        }

        private async Task GetSolarSystems()
        {
            if (SkanRange.IsValid())
            {
                PbData.ActualValue = 0;
                PbData.MaxValue = CountElementsToDownload();
                await Task.Run(async () =>
                {
                    var gameFileReader = new OgamexFileReader();
                    var dataManager = new UserPlanetDataManager(usersPlanets);
                    string solarSysteFile;
                    var random = new Random();
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
                                await gameFileReader.AddPlayersFromFile(solarSysteFile, usersPlanets, DateTime.Now, actualGalaxy, actualSolarSystem);
                                ActualPositionReaded = actualGalaxy + ":" + actualSolarSystem;
                                PbData.ActualValue++;
                                dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
                                await Task.Delay(400 + random.Next(0, 200));
                            }

                        dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
                        MessageBox.Show("Saving data finished");
                    }
                    catch (RestException e)
                    {
                        dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
                        MessageBox.Show(e.Message);
                    }
                    catch (Exception)
                    {
                        dataManager.SaveIntoXmlFile("Database" + _gameRestClient.GetGameType() + _gameRestClient.GetUniversum());
                        MessageBox.Show("Unknow exception, contact with developer");
                    }
                });
            }
            else
                MessageBox.Show("Wrong data");
        }

        private void SaveTokenAndSessionId()
        {
            var token = new Token(_gameRestClient.GetGameType(), _gameRestClient.GetUniversum());
            token.SaveToken(Token);
            token.SaveSessionId(SessionId);
            Token = "";
            SessionId = "";
        }


        private void ShowGetTokenHelp()
        {
            var gifWindow = new GetTokenGifView();
            gifWindow.Show();
        }

        #endregion

        #region Commands

        public DelegateCommand GetSolarSystemsDataCommand { set; get; }

        public DelegateCommand SaveTokenCommand { set; get; }

        public DelegateCommand ShowGetTokenHelpCommand { set; get; }

        #endregion
    }
}