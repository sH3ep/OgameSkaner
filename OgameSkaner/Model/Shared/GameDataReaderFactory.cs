using OgameSkaner.Model.Sgame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OgameSkaner.Model.Shared
{
    class GameDataReaderFactory
    {

        public IGameFileReader CreateFileReader(GameType gameType)
        {
            switch (gameType)
            {
                case GameType.IWgame:
                    return new IWgameFileReader();


                case GameType.Sgame:
                    return new SgameFileReader();

                default:
                    return new SgameFileReader();
            }
        }
    }
}
