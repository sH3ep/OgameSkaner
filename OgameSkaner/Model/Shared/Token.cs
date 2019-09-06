using System;
using System.Configuration;
using System.Threading;
using OgameSkaner.Utils;

namespace OgameSkaner.Model
{
    public class Token
    {
        private string _token;
        private GameType _gameType;

        public Token(GameType gameType)
        {
            _gameType = gameType;
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

        public void SaveToken(string token)
        {
            var test = _gameType.ToString() + "token";
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var encryptedToken = EncryptionHelper.Encrypt(token);
            config.AppSettings.Settings[_gameType.ToString()+"token"].Value = encryptedToken;
            config.Save(ConfigurationSaveMode.Modified);
            config.Save();
            
            ConfigurationManager.RefreshSection("appSettings");
        }

        public string GetToken()
        {
            var encryptedToken = ConfigurationManager.AppSettings.Get((_gameType.ToString()+"token"));
            var token = EncryptionHelper.Decrypt(encryptedToken);
            return token;
        }

        public void Delete()
        {
            SaveToken("");
        }
    }
}