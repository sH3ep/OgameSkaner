//using OgameSkaner.Model;
//using RestSharp;

//namespace OgameSkaner.RestClient
//{
//    public class RequestConfigurator
//    {
//        public RequestConfigurator(string universum)
//        {
//            _universum = universum;
//        }
//        private string _universum;

//        public RestRequest Configure(RequestType requestType)
//        {
//            switch (requestType)
//            {
//                case RequestType.GetSolarSystem:
//                    return GetSolarSystemRequestConfiguration();
//                case RequestType.Login:
//                    return GetLoginRequestConfiguration();
//                case RequestType.SpyPlanet:
//                    return GetSpyReportReqestConfiguration();
//                default:
//                    return GetStartPageRequestConfiguration();
//            }
//        }

//        private RestRequest GetSpyReportReqestConfiguration()
//        {
//            var request = new RestRequest("https://uni"+_universum+".sgame.pl/game.php?", Method.GET);
//            request.AddHeader("authority", "uni2.sgame.pl");
//            request.AddHeader("method", "GET");

//            request.AddHeader("scheme", "https");
//            request.AddHeader("accept",
//                "application/json, text/javascript, */*; q=0.01");
//            request.AddHeader("accept-encoding", "gzip, deflate, br");
//            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
//            request.AddHeader("referer", "https://uni" + _universum + ".sgame.pl/game.php?page=galaxy");
//            request.AddHeader("sec-fetch-mode", "cors");
//            request.AddHeader("sec-fetch-site", "same-origin");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");



//            var token = new Token(GameType.Sgame,_universum);
//            request.AddCookie("lang", "pl");
//            request.AddCookie("scroll", "0");
//            request.AddCookie("2Moons", token.GetToken());

//            request.AddQueryParameter("page", "fleetAjax");
//            request.AddQueryParameter("ajax", "1");
//            request.AddQueryParameter("mission", "6");

//            return request;
//        }

//        private RestRequest GetStartPageRequestConfiguration()
//        {
//            var request = new RestRequest("game.php?page=overview", Method.GET);
//            request.AddHeader("authority", "uni2.sgame.pl");
//            request.AddHeader("method", "GET");
//            request.AddHeader("path", "/game.php?page=overview");
//            request.AddHeader("scheme", "https");
//            request.AddHeader("accept",
//                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
//            request.AddHeader("accept-encoding", "gzip, deflate, br");
//            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
//            request.AddHeader("referer", "https://uni" + _universum + ".sgame.pl/game.php?page=overview");
//            request.AddHeader("sec-fetch-mode", "navigate");
//            request.AddHeader("sec-fetch-site", "same-origin");
//            request.AddHeader("sec-fetch-user", "?1");
//            request.AddHeader("upgrade-insecure-requests", "1");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

//            request.AddQueryParameter("page", "overview");
//            var token = new Token(GameType.Sgame, _universum);
//            request.AddCookie("lang", "pl");
//            request.AddCookie("scroll", "0");
//            request.AddCookie("2Moons", token.GetToken());

//            return request;
//        }

//        private RestRequest GetLoginRequestConfiguration()
//        {
//            var request = new RestRequest("index.php?page=login", Method.POST);
//            request.AddHeader("authority", "uni2.sgame.pl");
//            request.AddHeader("method", "POST");
//            request.AddHeader("path", "/index.php?page=login");
//            request.AddHeader("scheme", "https");
//            request.AddHeader("accept",
//                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
//            request.AddHeader("accept-encoding", "gzip, deflate, br");
//            request.AddHeader("accept-language", "pl - PL, pl; q = 0.9,en - US; q = 0.8,en; q = 0.7");
//            request.AddHeader("cache-control", "max-age=0");
//            request.AddHeader("content-length", "39");
//            request.AddHeader("content-type", "application/x-www-form-urlencoded");
//            request.AddHeader("referer", "https://sgame.pl/");
//            request.AddHeader("origin", "https://uni" + _universum + ".sgame.pl");
//            request.AddHeader("sec-fetch-mode", "navigate");
//            request.AddHeader("sec-fetch-site", "same-site");
//            request.AddHeader("sec-fetch-user", "?1");
//            request.AddHeader("upgrade-insecure-requests", "1");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

//            request.AddCookie("lang", "pl");
//            request.AddCookie("scroll", "0");


//            var token = new Token(GameType.Sgame,_universum);
//            token.SaveToken(token.GenerateToken());

//            request.AddCookie("2Moons", token.GetToken());

//            request.AddQueryParameter("page", "login");

//            return request;
//        }

//        private RestRequest GetSolarSystemRequestConfiguration()
//        {
//            var request = new RestRequest("https://" + _universum + ".ogamex.net/galaxy/galaxydata", Method.GET);
//            request.AddHeader("accept", "*/*");
//            request.AddHeader("Accept-Encoding", "gzip, deflate, br");
//            request.AddHeader("Connection", "keep-alive");
//            request.AddHeader("authority", "orion.ogamex.net");
//            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//            request.AddHeader("referer", "https://orion.ogamex.net/galaxy");
//            request.AddHeader("sec-ch-ua", " \"Google Chrome\";v=\"113\", \"Chromium\";v=\"113\", \"Not - A.Brand\";v=\"24\" ");
//            request.AddHeader("sec-ch-ua-mobile", "?0");
//            request.AddHeader("sec-ch-ua-platform", "\"Windows\"");
//            request.AddHeader("sec-fetch-dest", "empty");
//            request.AddHeader("sec-fetch-mode", "cors");
//            request.AddHeader("sec-fetch-site", "same-origin");
//            request.AddHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36");
//            request.AddHeader("x-requested-with", "XMLHttpRequest");

//            var token = new Token(GameType.OgameX, _universum).GetToken();

//            request.AddCookie("lang", "pl");
//            request.AddCookie("timeZoneOffset", "120");
//            request.AddCookie("SessionId", "iafebx44bs5vx4f3v41ndm4w");
//            request.AddCookie("gameAuthToken", token);
//            request.AddCookie("_ga", "GA1.1.1428976385.1681921793");
//         //   request.AddCookie("__cf_bm", "0");
//            request.AddCookie("_ga_EB8YK2X9FG", "GS1.1.1683777122.65.1.1683785625.0.0.0");

//            return request;
//        }
//    }
//}