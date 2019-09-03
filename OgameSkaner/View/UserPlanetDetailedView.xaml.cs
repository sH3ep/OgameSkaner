using System.Windows.Controls;
using OgameSkaner.Model;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    /// Interaction logic for UserPlanetDetailedView.xaml
    /// </summary>
    public partial class UserPlanetDetailedView : UserControl
    {


        public UserPlanetDetailedView(UserPlanet userPlanetData)
        {
            
            this.DataContext = new UserPlanetDetailedViewModel();
            ((UserPlanetDetailedViewModel)DataContext).UserPlanetData = userPlanetData;
            InitializeComponent();
        }
    }
}
