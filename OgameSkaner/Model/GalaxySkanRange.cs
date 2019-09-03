namespace OgameSkaner.Model
{
    public class GalaxySkanRange
    {
        private int _startGalaxy=1;
        private int _startSystem=1;
        private int _endGalaxy=1;
        private int _endSystem=1;

        public int StartGalaxy
        {
            get { return _startGalaxy; }
            set { _startGalaxy = value; }
        }

        public int StartSystem
        {
            get { return _startSystem; }
            set { _startSystem = value; }
        }

        public int EndGalaxy
        {
            get { return _endGalaxy; }
            set { _endGalaxy = value; }
        }

        public int EndSystem
        {
            get { return _endSystem; }
            set { _endSystem = value; }
        }

        public bool IsValid()
        {
            if (_startGalaxy > _endGalaxy)
            {
                return false;
            }

            if(_startSystem > _endSystem)
            {
                return false;
            }

            if (_startGalaxy < 1 || _startGalaxy > 7)
            {
                return false;
            }

            if (_startSystem < 1 || _startSystem > 499)
            {
                return false;
            }

            if (_endGalaxy < 1 || _endGalaxy > 7)
            {
                return false;
            }

            if (_endSystem < 1 || _endSystem > 499)
            {
                return false;
            }

            return true;

        }
    }
}
