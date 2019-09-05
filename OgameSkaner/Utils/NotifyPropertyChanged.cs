using System.ComponentModel;
using System.Windows;

namespace OgameSkaner.Utils
{
    public class NotifyPropertyChanged : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void RaisePropertyChanged(string property)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
    }
}