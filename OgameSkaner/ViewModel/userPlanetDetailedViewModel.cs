using System;
using System.Threading;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetDetailedViewModel : NotifyPropertyChanged
    {
        private string _spySuccededRectangleColor;
        private string _spySuccededRectangleToolTip;
        private UserPlanet _userPlanet;
        private GameType gameType = GameType.IWgame;

        public UserPlanetDetailedViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient; 
            SpyPlanetCommand = new DelegateCommand(SpyPlanet, CanExecuteSpy);
        }

        private IGameRestClient _gameRestClient;

        public DelegateCommand SpyPlanetCommand { set; get; }

        public UserPlanet UserPlanetData
        {
            get => _userPlanet;
            set
            {
                _userPlanet = value;
                RaisePropertyChanged("UserPlanetData");
                SpyPlanetCommand.RaiseCanExecuteChanged();
            }
        }

        public string SpySuccededRectangleColor
        {
            set
            {
                _spySuccededRectangleColor = value;
                RaisePropertyChanged("SpySuccededRectangleColor");
            }
            get => _spySuccededRectangleColor;
        }

        public string SpySuccededRectangleToolTip
        {
            set
            {
                _spySuccededRectangleToolTip = value;
                RaisePropertyChanged("SpySuccededRectangleToolTip");
            }
            get => _spySuccededRectangleToolTip;
        }

        private bool CanExecuteSpy()
        {
            if (_userPlanet != null)
                if (_userPlanet.PlanetId > 0)
                    return true;
            return false;
        }

        private void SpyPlanet()
        {
            try
            {
                _gameRestClient.SpyPlanet(_userPlanet,PlanetType.PLANET);
                
                SpySuccededRectangleColor = "green";
                SpySuccededRectangleToolTip = "Spy succeeded";
                _gameRestClient.SpyPlanet(_userPlanet, PlanetType.MOON);
            }
            catch (Exception)
            {
                SpySuccededRectangleColor = "red";
                SpySuccededRectangleToolTip = "Spy failed";
            }
        }
    }
}