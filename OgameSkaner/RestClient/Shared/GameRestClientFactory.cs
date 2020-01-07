using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.RestClient.InterWar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OgameSkaner.RestClient.Shared
{
   public class GameRestClientFactory
    {
        public IGameRestClient CreateRestClient(GameType gameType, int universum)
        {
            switch (gameType)
            {
                case GameType.IWgame:
                    return new IWgameRestClient(universum);
                    

                case GameType.Sgame:
                    return new SgameRestClient(universum);

                default:
                    return new SgameRestClient(1);
            }
        }
    }
}
