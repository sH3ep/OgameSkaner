using OgameSkaner.ViewModel;
using System.Windows;

namespace OgameSkaner.View
{
    /// <summary>
    /// Interaction logic for ManageConfigurationView.xaml
    /// </summary>
    public partial class ManageConfigurationView : Window
    {
        public ManageConfigurationView()
        {
            InitializeComponent();
            DataContext = new ManageConfigurationViewModel();
        }

        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }
}
