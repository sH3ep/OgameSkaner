using System;
using System.Configuration;
using RestSharp;

namespace OgameSkaner.Model
{
    public class RequestConfigurator
    {
       
        public RequestConfigurator()
        {
           
        }

        public RestRequest Configure(RequestType requestType)
        {
            switch (requestType)
            {
                case RequestType.GetSolarSystem:
                    return GetSolarSystemRequestConfiguration();
                    break;
                case RequestType.Login:
                    return GetLoginRequestConfiguration();
                    break;
                case RequestType.SpyPlanet:
                    return GetSpyReqestConfiguration();
                    break;
                default:
                    return GetStartPageRequestConfiguration();
                    break; 
            }
        }

        private RestRequest GetStartPageRequestConfiguration()
        {
            var _request = new RestRequest("game.php?page=overview", Method.GET);
            _request.AddHeader("authority", "uni2.sgame.pl");
            _request.AddHeader("method", "GET");
            _request.AddHeader("path", "/game.php?page=overview");
            _request.AddHeader("scheme", "https");
            _request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            _request.AddHeader("accept-encoding", "gzip, deflate, br");
            _request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
            _request.AddCookie("lang", "pl");
            _request.AddCookie("scroll", "0");
            _request.AddCookie("2Moons", "6qa24isbmkd4fgj40utvvctbsr");
            _request.AddHeader("referer", "https://uni2.sgame.pl/game.php?page=overview");
            _request.AddHeader("sec-fetch-mode", "navigate");
            _request.AddHeader("sec-fetch-site", "same-origin");
            _request.AddHeader("sec-fetch-user", "?1");
            _request.AddHeader("upgrade-insecure-requests", "1");
            _request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            _request.AddQueryParameter("page", "overview");

            return _request;
        }

        private RestRequest GetSpyReqestConfiguration()
        {
            throw new NotImplementedException();
        }

        private RestRequest GetLoginRequestConfiguration()
        {
            throw new NotImplementedException();
        }

        private RestRequest GetSolarSystemRequestConfiguration()
        {
            var _request = new RestRequest("https://uni2.sgame.pl/game.php?page=galaxy", Method.POST);
            _request.AddHeader("authority", "uni2.sgame.pl");
            _request.AddHeader("method", "POST");
            _request.AddHeader("path", "/game.php?page=galaxy");
            _request.AddHeader("scheme", "https");
            _request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            _request.AddHeader("accept-encoding", "gzip, deflate, br");
            _request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
            _request.AddHeader("cache-control", "max-age=0");
            _request.AddHeader("content-length", "32");
            _request.AddHeader("content-type", "application/x-www-form-urlencoded");
            _request.AddHeader("origin", "https://uni2.sgame.pl");
            _request.AddHeader("referer", "https://uni2.sgame.pl/game.php?page=galaxy");
            _request.AddHeader("sec-fetch-mode", "navigate");
            _request.AddHeader("sec-fetch-site", "same-origin");
            _request.AddHeader("sec-fetch-user", "?1");
            _request.AddHeader("upgrade-insecure-requests", "1");
            _request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            _request.AddCookie("lang", "pl");
            _request.AddCookie("scroll", "0");
            var token = ConfigurationManager.AppSettings.Get("token");
            _request.AddCookie("2Moons", token);

            return _request;
        }
    }
}