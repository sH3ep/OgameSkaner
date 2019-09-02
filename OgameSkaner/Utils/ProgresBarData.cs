using System.Drawing;

namespace OgameSkaner.Model
{
    public class ProgresBarData:NotifyPropertyChanged
    {
        private int _actualValue;

        public int ActualValue
        {
            set
            {
                _actualValue = value;
                RaisePropertyChanged("ActualValue");
            }
            get { return _actualValue; }
        }

        private int _maxValue;

        public int MaxValue
        {
            set
            {
                _maxValue = value;
                RaisePropertyChanged("MaxValue");
            }
            get { return _maxValue; }
        }

        private Brush _color = Brushes.Green;
        public Brush Color
        {
            set
            {
                _color = value;
                RaisePropertyChanged("Color");

            }
            get { return _color; }
        } 
    }
}