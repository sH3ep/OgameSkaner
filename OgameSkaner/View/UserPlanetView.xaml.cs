using System.Windows.Controls;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    ///     Interaction logic for UserPlanetView.xaml
    /// </summary>
    public partial class UserPlanetView : UserControl
    {
        public UserPlanetView()
        {
            DataContext = new UserPlanetViewModel();
            InitializeComponent();
        }
    }
}