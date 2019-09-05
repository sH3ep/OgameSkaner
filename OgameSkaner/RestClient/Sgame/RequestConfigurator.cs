using OgameSkaner.Model;
using RestSharp;

namespace OgameSkaner.RestClient
{
    public class RequestConfigurator
    {
        public RestRequest Configure(RequestType requestType)
        {
            switch (requestType)
            {
                case RequestType.GetSolarSystem:
                    return GetSolarSystemRequestConfiguration();
                case RequestType.Login:
                    return GetLoginRequestConfiguration();
                case RequestType.SpyPlanet:
                    return GetSpyReportReqestConfiguration();
                default:
                    return GetStartPageRequestConfiguration();
            }
        }

        private RestRequest GetSpyReportReqestConfiguration()
        {
            var request = new RestRequest("https://uni2.sgame.pl/game.php?", Method.GET);
            request.AddHeader("authority", "uni2.sgame.pl");
            request.AddHeader("method", "GET");

            request.AddHeader("scheme", "https");
            request.AddHeader("accept",
                "application/json, text/javascript, */*; q=0.01");
            request.AddHeader("accept-encoding", "gzip, deflate, br");
            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
            request.AddHeader("referer", "https://uni2.sgame.pl/game.php?page=galaxy");
            request.AddHeader("sec-fetch-mode", "cors");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");


            var token = new Token();
            request.AddCookie("lang", "pl");
            request.AddCookie("scroll", "0");
            request.AddCookie("2Moons", token.GetToken());

            request.AddQueryParameter("page", "fleetAjax");
            request.AddQueryParameter("ajax", "1");
            request.AddQueryParameter("mission", "6");

            return request;
        }

        private RestRequest GetStartPageRequestConfiguration()
        {
            var request = new RestRequest("game.php?page=overview", Method.GET);
            request.AddHeader("authority", "uni2.sgame.pl");
            request.AddHeader("method", "GET");
            request.AddHeader("path", "/game.php?page=overview");
            request.AddHeader("scheme", "https");
            request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            request.AddHeader("accept-encoding", "gzip, deflate, br");
            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
            request.AddHeader("referer", "https://uni2.sgame.pl/game.php?page=overview");
            request.AddHeader("sec-fetch-mode", "navigate");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("sec-fetch-user", "?1");
            request.AddHeader("upgrade-insecure-requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddQueryParameter("page", "overview");
            var token = new Token();
            request.AddCookie("lang", "pl");
            request.AddCookie("scroll", "0");
            request.AddCookie("2Moons", token.GetToken());

            return request;
        }

        private RestRequest GetLoginRequestConfiguration()
        {
            var request = new RestRequest("index.php", Method.POST);
            request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            request.AddHeader("accept-encoding", "gzip, deflate");
            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
            request.AddHeader("cache-control", "max-age=0");
            request.AddHeader("Connection", "keep-alive");
            request.AddHeader("content-length", "63");
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddHeader("Host", "www.inter-war.com.pl");
            request.AddHeader("origin", "https://uni2.sgame.pl");

            request.AddHeader("authority", "uni2.sgame.pl");
            request.AddHeader("method", "POST");
            request.AddHeader("path", "/index.php?page=login");
            request.AddHeader("scheme", "https");

            
            
            
            request.AddHeader("referer", "https://sgame.pl/");
            
            request.AddHeader("sec-fetch-mode", "navigate");
            request.AddHeader("sec-fetch-site", "same-site");
            request.AddHeader("sec-fetch-user", "?1");
            request.AddHeader("upgrade-insecure-requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddCookie("lang", "pl");
            request.AddCookie("scroll", "0");


            var token = new Token();
            token.SaveToken(token.GenerateToken());

            request.AddCookie("2Moons", token.GetToken());

            request.AddQueryParameter("page", "login");

            return request;
        }

        private RestRequest GetSolarSystemRequestConfiguration()
        {
            var request = new RestRequest("https://uni2.sgame.pl/game.php?page=galaxy", Method.POST);
            request.AddHeader("authority", "uni2.sgame.pl");
            request.AddHeader("method", "POST");
            request.AddHeader("path", "/game.php?page=galaxy");
            request.AddHeader("scheme", "https");
            request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            request.AddHeader("accept-encoding", "gzip, deflate, br");
            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
            request.AddHeader("cache-control", "max-age=0");
            request.AddHeader("content-length", "32");
            request.AddHeader("content-type", "application/x-www-form-urlencoded");
            request.AddHeader("origin", "https://uni2.sgame.pl");
            request.AddHeader("referer", "https://uni2.sgame.pl/game.php?page=galaxy");
            request.AddHeader("sec-fetch-mode", "navigate");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("sec-fetch-user", "?1");
            request.AddHeader("upgrade-insecure-requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddCookie("lang", "pl");
            request.AddCookie("scroll", "0");
            var token = new Token().GetToken();
            request.AddCookie("2Moons", token);

            return request;
        }
    }
}