using System.Drawing;

namespace OgameSkaner.Utils
{
    public class ProgresBarData : NotifyPropertyChanged
    {
        private int _actualValue;

        private Brush _color = Brushes.Green;

        private int _maxValue;

        public int ActualValue
        {
            set
            {
                _actualValue = value;
                RaisePropertyChanged("ActualValue");
            }
            get => _actualValue;
        }

        public int MaxValue
        {
            set
            {
                _maxValue = value;
                RaisePropertyChanged("MaxValue");
            }
            get => _maxValue;
        }

        public Brush Color
        {
            set
            {
                _color = value;
                RaisePropertyChanged("Color");
            }
            get => _color;
        }
    }
}