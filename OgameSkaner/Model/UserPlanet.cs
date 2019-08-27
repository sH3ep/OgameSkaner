using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Annotations;

namespace OgameSkaner.Model
{
    [Serializable()]
    public class UserPlanet  
    {
        public UserPlanet() { }

        public UserPlanet(string userName, string planetLocalization)
        {
            _userName = userName;
            _galaxy = GetGalaxy(planetLocalization);
            _solarSystem = GetSolarSystem(planetLocalization);
            _localization = _galaxy + ":" + _solarSystem;

        }

        public UserPlanet(string userName, string planetLocalization,DateTime creationDate)
        {
            _userName = userName;
            _galaxy = GetGalaxy(planetLocalization);
            _solarSystem = GetSolarSystem(planetLocalization);
            _localization = _galaxy + ":" + _solarSystem;
            _creationDate = creationDate;

        }

        public UserPlanet(string userName, string planetLocalization,int position, DateTime creationDate)
        {
            _userName = userName;
            _galaxy = GetGalaxy(planetLocalization);
            _solarSystem = GetSolarSystem(planetLocalization);
            _position = position;
            _localization = _galaxy + ":" + _solarSystem +":"+_position;
            _creationDate = creationDate;

        }

        #region private fields

        private string _userName;
        private int _galaxy;
        private int _solarSystem;
        private string _localization;
        private bool _toDelete;
        private DateTime _creationDate;
        private int _position;

        #endregion

        #region Public properties

        public DateTime CreationDate
        {
            get { return _creationDate; }
            set { _creationDate = value; }
        }


        public int Galaxy
        {
            get => _galaxy;
            set => _galaxy = value;
        }

        public int SolarSystem
        {
            get => _solarSystem;
            set => _solarSystem = value;
        }

        public int Position
        {
            get => _position;
            set => _position = value;
        }

        public string UserName
        {
            get => _userName;
            set => _userName = value;
        }

        public bool ToDelete
        {
            get => _toDelete;
            set => _toDelete = value;
        }

        public string Localization
        {
            get => _localization;
            set => _localization = value;
        }

        #endregion

        #region private Methods

        private int GetGalaxy(string localization)
        {
            foreach (char c in localization)
            {
                if (char.IsNumber(c))
                {
                    return c - '0';
                }
            }

            return 0;
        }

        private int GetSolarSystem(string localization)
        {
            bool galaxyReded = false;
            string solarSystemString = "";
            foreach (char c in localization)
            {


                if (char.IsNumber(c) && galaxyReded)
                {
                    solarSystemString = solarSystemString + c;
                }

                if (char.IsNumber(c) && !galaxyReded)
                {
                    galaxyReded = true;
                }
            }

            try
            {
                int solarSystem = int.Parse(solarSystemString);
                return solarSystem;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 0;
            }


        }

        #endregion
    }
}