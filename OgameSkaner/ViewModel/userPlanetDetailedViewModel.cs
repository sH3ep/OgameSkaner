using OgameSkaner.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OgameSkaner.ViewModel
{
    public class UserPlanetDetailedViewModel : NotifyPropertyChanged
    {
        private UserPlanet _userPlanet;

        public UserPlanet UserPlanetData
        {
            get { return _userPlanet; }
            set
            {
                _userPlanet = value;
                RaisePropertyChanged("UserPlanetData");
            }
        }



    }
}
