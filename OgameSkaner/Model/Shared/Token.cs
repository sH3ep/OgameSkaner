using System;
using System.Configuration;
using System.Threading;
using OgameSkaner.Model.GameConfiguration;
using OgameSkaner.Utils;

namespace OgameSkaner.Model
{
    public class Token
    {
        private string _token;
        private GameType _gameType;
        private int _universum;

        public Token(GameType gameType,int universum)
        {
            _gameType = gameType;
            _universum = universum;
        }

        public string GenerateToken()
        {
          
            var token = "";

            for (var i = 1; i < 27; i++) token = token + GenerateRandomLetterOrNumber();

            _token = token;
            return token;
        }


        private string GenerateRandomLetterOrNumber()
        {
            var random = new Random((int) DateTime.Now.Ticks);
            Thread.Sleep(2);
            var chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZqwertyuioplkjhgfdsazxcvbnm";
            var ch = chars[random.Next(chars.Length)].ToString();
            return ch;
        }

        public void SaveToken_old(string token)
        {
            var test = _gameType.ToString() + "token";
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var encryptedToken = EncryptionHelper.Encrypt(token);
            config.AppSettings.Settings[_gameType.ToString()+"token"].Value = encryptedToken;
            config.Save(ConfigurationSaveMode.Modified);
            config.Save();
            
            ConfigurationManager.RefreshSection("appSettings");
        }

        public void SaveToken(string token)
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            gameConfiguration.Token = token;
            gameConfigSerializer.AddConfiguration(gameConfiguration);
        }

        public string GetToken()
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            return gameConfiguration.Token;
        }

        public void Delete()
        {
            SaveToken("");
        }
    }
}