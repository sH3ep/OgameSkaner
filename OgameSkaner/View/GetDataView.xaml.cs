﻿using System.Windows;
using System.Windows.Controls;
using OgameSkaner.RestClient;
using OgameSkaner.ViewModel;

namespace OgameSkaner.View
{
    /// <summary>
    ///     Interaction logic for GetDataView.xaml
    /// </summary>
    public partial class GetDataView : UserControl
    {
        public GetDataView(IGameRestClient gameRestClient)
        {
            InitializeComponent();
            DataContext = new GetDataViewModel(gameRestClient);
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext != null) ((dynamic) DataContext).SecurePassword = ((PasswordBox) sender).SecurePassword;
        }
    }
}