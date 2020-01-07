using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OgameSkaner.Model.Shared
{
    interface IGameFileReader
    {
        Task AddPlayersFromFile(string fileText, ObservableCollection<UserPlanet> playersPlanets,
          DateTime fileCreationDate);
    }
}
