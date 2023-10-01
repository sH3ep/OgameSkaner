using OgameSkaner.Model;
using OgameSkaner.RestClient.InterWar;
using System;

namespace OgameSkaner.RestClient.Shared
{
    public class GameRestClientFactory
    {
        public IGameRestClient CreateRestClient(GameType gameType, string universum)
        {
            switch (gameType)
            {
                //case GameType.IWgame:
                //    return new IWgameRestClient(universum);


                //case GameType.Sgame:
                //    return new SgameRestClient(universum);


                case GameType.OgameX:
                    return new OgameXRestClient(universum);

                default:
                    throw new ArgumentException("Wrong game type");
            }
        }
    }
}
