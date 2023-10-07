namespace OgameSkaner.RestClient.OgameX
{
    public class SpyPlanetRequest
    {
        public SpyPlanetRequest(int galaxy, int system, int position, bool isMoon = false)
        {
            X = galaxy;
            Y = system;
            Z = position;
            PlanetType = isMoon ? 2 : 1;
        }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int PlanetType { get; set; }
    }


}
