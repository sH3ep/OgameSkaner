using System;
using System.Xml.Serialization;

namespace OgameSkaner.Model
{
    [Serializable]
    public class GameConfiguration
    {
        public GameType GameType { get; set; }
        public int Universum { get; set; }
        public int SpyProbeAmount { set; get; }
        public string CurrentPlanet { set; get; }
        public string BaseUri { set; get; }

        [XmlIgnore]
        public string ConfigurationName
        {
            get { return GameType.ToString() + " Universum " + Universum; }
            private set { } 
        }
    }
}