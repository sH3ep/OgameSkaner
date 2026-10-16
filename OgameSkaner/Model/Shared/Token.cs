using OgameSkaner.Model.GameConfiguration;

namespace OgameSkaner.Model
{
    public class Token
    {
        private GameType _gameType;
        private string _universum;

        public Token(GameType gameType, string universum)
        {
            _gameType = gameType;
            _universum = universum;
        }

        public Token(GameType gameType, int universum)
        {
            _gameType = gameType;
            _universum = universum.ToString();
        }

        public void SaveToken(string token)
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            gameConfiguration.Token = token;
            gameConfigSerializer.AddConfiguration(gameConfiguration);
        }

        public void SaveSessionId(string sessionId)
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            gameConfiguration.SessionId = sessionId;
            gameConfigSerializer.AddConfiguration(gameConfiguration);
        }

        public string GetToken()
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            return gameConfiguration.Token;
        }

        public string GetSessionId()
        {
            var gameConfigSerializer = new GamesConfigurationSerializer();
            var gameConfiguration = gameConfigSerializer.GetConfiguration(_gameType, _universum);
            return gameConfiguration.SessionId;
        }

        public void Delete()
        {
            SaveToken("");
        }
    }
}