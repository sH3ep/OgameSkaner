﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
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
