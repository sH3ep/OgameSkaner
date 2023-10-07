using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;
using Prism.Commands;
using System;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetDetailedViewModel : NotifyPropertyChanged
    {
        private string _spySuccededRectangleColor;
        private string _spyMoonSuccededRectangleColor;
        private string _spySuccededRectangleToolTip;
        private UserPlanet _userPlanet;

        public UserPlanetDetailedViewModel(IGameRestClient gameRestClient)
        {
            _gameRestClient = gameRestClient;
            SpyPlanetCommand = new DelegateCommand(SpyPlanet, CanExecuteSpy);
            SpyMoonCommand = new DelegateCommand(SpyMoon, CanExecuteMoonSpy);
        }

        private IGameRestClient _gameRestClient;

        public DelegateCommand SpyPlanetCommand { set; get; }
        public DelegateCommand SpyMoonCommand { set; get; }

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

        public string SpyMoonSuccededRectangleColor
        {
            set
            {
                _spyMoonSuccededRectangleColor = value;
                RaisePropertyChanged("SpyMoonSuccededRectangleColor");
            }
            get => _spyMoonSuccededRectangleColor;
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

            if (_gameRestClient.GetGameType() == GameType.OgameX)
            {
                return true;
            }

            if (_userPlanet != null)
                if (_userPlanet.PlanetId > 0)
                    return true;
            return false;
        }

        private bool CanExecuteMoonSpy()
        {

            if (_gameRestClient.GetGameType() == GameType.OgameX)
            {

                return _userPlanet.HasMoon;
            }

            if (_userPlanet != null)
                if (_userPlanet.PlanetId > 0 && _userPlanet.HasMoon)
                    return true;
            return false;
        }

        private void SpyPlanet()
        {
            try
            {
                _gameRestClient.SpyPlanet(_userPlanet, PlanetType.PLANET);

                SpySuccededRectangleColor = "green";
                SpySuccededRectangleToolTip = "Spy succeeded";
            }
            catch (Exception)
            {
                SpySuccededRectangleColor = "red";
                SpySuccededRectangleToolTip = "Spy failed";
            }
        }

        private void SpyMoon()
        {
            try
            {
                SpyMoonSuccededRectangleColor = "green";
                _gameRestClient.SpyPlanet(_userPlanet, PlanetType.MOON);
            }
            catch (Exception)
            {
                SpyMoonSuccededRectangleColor = "red";
            }
        }
    }
}