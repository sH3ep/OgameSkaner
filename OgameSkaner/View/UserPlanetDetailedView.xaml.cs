using System.Windows.Controls;
using OgameSkaner.Model;
using OgameSkaner.RestClient;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    ///     Interaction logic for UserPlanetDetailedView.xaml
    /// </summary>
    public partial class UserPlanetDetailedView : UserControl
    {
        public UserPlanetDetailedView(UserPlanet userPlanetData,IGameRestClient gameRestClient)
        {
            DataContext = new UserPlanetDetailedViewModel(gameRestClient);
            ((UserPlanetDetailedViewModel) DataContext).UserPlanetData = userPlanetData;
            InitializeComponent();
        }
    }
}