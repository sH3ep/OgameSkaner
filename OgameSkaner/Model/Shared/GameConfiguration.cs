using OgameSkaner.Utils;
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
        public string DataFileName { set; get; }
        public string Login { set; get; }
        public string EncryptedToken { set; get; }

        [XmlIgnore]
        public string Token
        {
            set
            {
                EncryptedToken = EncryptionHelper.Encrypt(value);
            }
            get
            {
                return EncryptionHelper.Decrypt(EncryptedToken);
            }
        }

        [XmlIgnore]
        public string ConfigurationName
        {
            get { return GameType.ToString() + " Universum " + Universum; }
            private set { }
        }
    }
}