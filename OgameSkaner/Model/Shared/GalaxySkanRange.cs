namespace OgameSkaner.Model
{
    public class GalaxySkanRange
    {
        public int StartGalaxy { get; set; } = 1;

        public int StartSystem { get; set; } = 1;

        public int EndGalaxy { get; set; } = 1;

        public int EndSystem { get; set; } = 1;

        public bool IsValid()
        {
            if (StartGalaxy > EndGalaxy) return false;

            if (StartSystem > EndSystem) return false;

            if (StartGalaxy < 1 || StartGalaxy > 7) return false;

            if (StartSystem < 1 || StartSystem > 499) return false;

            if (EndGalaxy < 1 || EndGalaxy > 7) return false;

            if (EndSystem < 1 || EndSystem > 499) return false;

            return true;
        }
    }
}