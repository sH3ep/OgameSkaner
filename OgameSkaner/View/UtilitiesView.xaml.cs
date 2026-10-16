using System.Windows.Controls;
using OgameSkaner.RestClient;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    /// Interaction logic for UtilitiesView2.xaml
    /// </summary>
    public partial class UtilitiesView : UserControl
    {
        public UtilitiesView(IGameRestClient gameRestClient)
        {
            DataContext = new UtilitiesViewModel(gameRestClient);
            InitializeComponent();
        }
    }
}
