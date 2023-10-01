//using OgameSkaner.Model;
//using RestSharp;

//namespace OgameSkaner.RestClient.InterWar
//{
//    public class InterWarRequestConfigurator
//    {
//        private int _universum;
//        public InterWarRequestConfigurator(int universum)
//        {
//            _universum = universum;
//        }
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
//            var request = new RestRequest("game.php", Method.POST);
//            request.AddHeader("accept",
//                "*/*");
//            request.AddHeader("accept-encoding", "gzip, deflate");
//            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//            request.AddHeader("Connection", "keep-alive");
//            request.AddHeader("content-length", "59");
//            request.AddHeader("content-type", "application/x-www-form-urlencoded");
//            request.AddHeader("Host", "www.inter-war.com.pl");
//            request.AddHeader("origin", "http://www.inter-war.com.pl");
//            request.AddHeader("referer", "http://www.inter-war.com.pl/game.php?page=galaxy");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");
//            request.AddHeader("X-Requested-With", "XMLHttpRequest");
//            request.AddHeader("pmenu", "off");

//            request.AddCookie("__utmc", "184655217");
//            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
//            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");
//            request.AddQueryParameter("page", "fleetajax");
//            request.AddQueryParameter("ajax", "1");

//            request.AddCookie("2Moons_1036681298", "05jmaap5qsna2sdcemmrib4vo4");

//            var token = new Token(GameType.IWgame, _universum).GetToken();
//            request.AddCookie("2Moons_1036681297", token);

//            return request;
//        }

//        private RestRequest GetStartPageRequestConfiguration()
//        {
//            var request = new RestRequest("game.php", Method.GET);
//            request.AddHeader("accept",
//                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
//            request.AddHeader("accept-encoding", "gzip, deflate");
//            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//            request.AddHeader("Connection", "keep-alive");
//            request.AddHeader("Host", "www.inter-war.com.pl");
//            request.AddHeader("referer", "http://www.inter-war.com.pl/game.php");
//            request.AddHeader("Upgrade-Insecure-Requests", "1");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

//            request.AddCookie("__utmc", "184655217");
//            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
//            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");
//            request.AddCookie("menuleft_rozwoj", "on");
//            request.AddCookie("menuleft_nawigacja", "on");
//            request.AddCookie("menuleft_inne", "on");
//            request.AddCookie("pmenu", "on");




//            var token = new Token(GameType.IWgame,_universum);
//            var temp = token.GetToken();
//            request.AddCookie("2Moons_1036681297", token.GetToken());

//            request.AddQueryParameter("page", "overview");

//            return request;
//        }

//        private RestRequest GetLoginRequestConfiguration()
//        {
//            var request = new RestRequest("index.php", Method.POST);
//            request.AddHeader("accept",
//                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
//            request.AddHeader("accept-encoding", "gzip, deflate");
//            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//            request.AddHeader("cache-control", "max-age=0");
//            request.AddHeader("Connection", "keep-alive");
//            request.AddHeader("content-length", "63");
//            request.AddHeader("content-type", "application/x-www-form-urlencoded");
//            request.AddHeader("Host", "www.inter-war.com.pl");
//            request.AddHeader("origin", "http://www.inter-war.com.pl");
//            request.AddHeader("referer", "http://www.inter-war.com.pl/index.php?code=3");
//            request.AddHeader("Upgrade-Insecure-Requests", "1");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

//            request.AddCookie("__utmc", "184655217");
//            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
//            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");



//            var token = new Token(GameType.IWgame, _universum);
//            token.SaveToken(token.GenerateToken());

//            request.AddCookie("2Moons_1036681297", token.GetToken());



//            request.AddQueryParameter("page", "login");

//            return request;
//        }

//        private RestRequest GetSolarSystemRequestConfiguration()
//        {
//            var request = new RestRequest("game.php", Method.POST);
//            request.AddHeader("accept",
//                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3");
//            request.AddHeader("accept-encoding", "gzip, deflate");
//            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//            request.AddHeader("cache-control", "max-age=0");
//            request.AddHeader("Connection", "keep-alive");
//            request.AddHeader("content-length", "17");
//            request.AddHeader("content-type", "application/x-www-form-urlencoded");
//            request.AddHeader("Host", "www.inter-war.com.pl");
//            request.AddHeader("origin", "http://www.inter-war.com.pl");
//            request.AddHeader("referer", "http://www.inter-war.com.pl/game.php?page=galaxy");
//            request.AddHeader("Upgrade-Insecure-Requests", "1");
//            request.AddHeader("user-agent",
//                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

//            request.AddCookie("__utmc", "184655217");
//            request.AddCookie("__utmz", "184655217.1567709575.1.1.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=(not%20provided)");
//            request.AddCookie("__utma", "184655217.1467795021.1567709575.1567713022.1567777189.3");

//            request.AddCookie("lang", "pl");
//            request.AddCookie("scroll", "0");
//            var token = new Token(GameType.IWgame,_universum).GetToken();
//            request.AddCookie("2Moons_1036681297", token);

//            return request;
//        }
//    }
//}