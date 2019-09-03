using System.Windows;
using System.Windows.Controls;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    /// Interaction logic for GetDataView.xaml
    /// </summary>
    public partial class GetDataView : UserControl
    {
        public GetDataView()
        {
            InitializeComponent();
            this.DataContext = new GetDataViewModel();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext != null)
            { ((dynamic)this.DataContext).SecurePassword = ((PasswordBox)sender).SecurePassword; }
            
        }
    }
}
