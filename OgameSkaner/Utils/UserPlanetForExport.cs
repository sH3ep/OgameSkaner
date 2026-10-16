namespace OgameSkaner.Model
{
    public class UserPlanetForExport
    {
        public UserPlanetForExport()
        {
        }

        public UserPlanetForExport(UserPlanet userPlanet)
        {
            this.Galaxy = userPlanet.Galaxy;    
            this.Position = userPlanet.Position;
            this.SolarSystem = userPlanet.SolarSystem;
            this.UserName = userPlanet.UserName;
            this.Coordinates = $"{userPlanet.Galaxy}:{userPlanet.SolarSystem}:{userPlanet.Position}";
        }
        public string UserName { set; get; }

        public int Galaxy { set; get; }

        public int SolarSystem { set; get; }

        public int Position { set; get; }

        public string Coordinates { set; get; }


    }
}