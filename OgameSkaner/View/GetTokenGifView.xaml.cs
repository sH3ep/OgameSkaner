using System.IO;
using System.Windows;

namespace OgameSkaner.View
{
    /// <summary>
    ///     Interaction logic for GetTokenGifView.xaml
    /// </summary>
    public partial class GetTokenGifView : Window
    {
        public GetTokenGifView()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var gifLocation = Directory.GetCurrentDirectory() + "/Images/GetToken.gif";
            pictureBoxLoading.ImageLocation = gifLocation;
        }
    }
}