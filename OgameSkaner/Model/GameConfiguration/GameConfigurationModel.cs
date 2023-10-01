using System;
using System.Xml.Serialization;
using OgameSkaner.Utils;

namespace OgameSkaner.Model.GameConfiguration
{
    [Serializable]
    public class GameConfigurationModel
    {
        public GameType GameType { get; set; }
        public string Universum { get; set; }
        public int SpyProbeAmount { set; get; }
        public string CurrentPlanet { set; get; }
        public string BaseUri { set; get; }
        public string Login { set; get; }
        public string EncryptedToken { set; get; }
        public string SessionId { set; get; }

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