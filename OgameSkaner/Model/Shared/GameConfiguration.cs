using System;

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

        public string ConfigurationName
        {
            get { return GameType.ToString() + " " + Universum; }
            private set { } 
        }
    }
}