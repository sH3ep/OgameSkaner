using System.Windows.Controls;
using OgameSkaner.RestClient;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    ///     Interaction logic for UserPlanetView.xaml
    /// </summary>
    public partial class UserPlanetView : UserControl
    {
        public UserPlanetView(IGameRestClient gameRestClient)
        {
            DataContext = new UserPlanetViewModel(gameRestClient);
            InitializeComponent();
        }
    }
}