using System;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.Utils;
using Prism.Commands;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetDetailedViewModel : NotifyPropertyChanged
    {
        private UserPlanet _userPlanet;
        private string _spySuccededRectangleColor;
        private string _spySuccededRectangleToolTip;
        public DelegateCommand SpyPlanetCommand { set; get; }

        private bool CanExecuteSpy()
        {
            if (_userPlanet != null)
            {
                if (_userPlanet.PlanetId > 0)
                {
                    return true;
                }
            }
            return false;
        }
        public UserPlanet UserPlanetData
        {
            get { return _userPlanet; }
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
            get { return _spySuccededRectangleColor; }
        }

        public string SpySuccededRectangleToolTip
        {
            set
            {
                _spySuccededRectangleToolTip = value;
                RaisePropertyChanged("SpySuccededRectangleToolTip");
            }
            get
            {
                return _spySuccededRectangleToolTip;
            }
        }

        public UserPlanetDetailedViewModel()
        {
            SpyPlanetCommand = new DelegateCommand(SpyPlanet, CanExecuteSpy);
        }

        private void SpyPlanet()
        {
            var client = new SgameRestClient();
            try
            {
                client.SpyPlanet(_userPlanet);
                SpySuccededRectangleColor = "green";
                SpySuccededRectangleToolTip = "Spy succeeded";
            }
            catch (Exception)
            {
                SpySuccededRectangleColor = "red";
                SpySuccededRectangleToolTip = "Spy failed";
            }

        }



    }
}
