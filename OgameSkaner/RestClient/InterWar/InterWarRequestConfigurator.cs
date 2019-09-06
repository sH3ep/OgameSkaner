using OgameSkaner.Model;
using RestSharp;

namespace OgameSkaner.RestClient.InterWar
{
    public class InterWarRequestConfigurator
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


            var token = new Token(GameType.IWgame);
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
            var request = new RestRequest("game.php", Method.GET);
            request.AddHeader("accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
            request.AddHeader("accept-encoding", "gzip, deflate");
            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
            request.AddHeader("Connection", "keep-alive");
            request.AddHeader("Host", "www.inter-war.com.pl");
            request.AddHeader("referer", "http://www.inter-war.com.pl/game.php");
            request.AddHeader("Upgrade-Insecure-Requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddCookie("__utmc", "184655217");
            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");
            request.AddCookie("menuleft_rozwoj", "on");
            request.AddCookie("menuleft_nawigacja", "on");
            request.AddCookie("menuleft_inne", "on");
            request.AddCookie("pmenu", "on");




            var token = new Token(GameType.IWgame);
            var temp = token.GetToken();
            request.AddCookie("2Moons_1036681297", token.GetToken());

            request.AddQueryParameter("page", "overview");

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
            request.AddHeader("origin", "http://www.inter-war.com.pl");
            request.AddHeader("referer", "http://www.inter-war.com.pl/index.php?code=3");
            request.AddHeader("Upgrade-Insecure-Requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddCookie("__utmc", "184655217");
            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");



            var token = new Token(GameType.IWgame);
            token.SaveToken(token.GenerateToken());

            request.AddCookie("2Moons_1036681297", token.GetToken());
            


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
            var token = new Token(GameType.IWgame).GetToken();
            request.AddCookie("2Moons", token);

            return request;
        }
    }
}