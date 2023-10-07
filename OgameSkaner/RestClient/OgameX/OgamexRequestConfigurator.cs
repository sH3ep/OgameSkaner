using OgameSkaner.Model;
using OgameSkaner.RestClient.OgameX;
using RestSharp;

namespace OgameSkaner.RestClient
{
    public class OgamexRequestConfigurator
    {
        public OgamexRequestConfigurator(string universum)
        {
            _universum = universum;
        }
        private string _universum;
        public RestRequest Configure(RequestType requestType)
        {
            switch (requestType)
            {
                case RequestType.GetSolarSystem:
                    return GetSolarSystemRequestConfiguration();
                case RequestType.Login:
                    return GetLoginRequestConfiguration();

                default:
                    return GetStartPageRequestConfiguration();
            }
        }

        public RestRequest GetSpyReportReqestConfiguration(SpyPlanetRequest requestBody)
        {
            var request = new RestRequest("https://" + _universum + ".ogamex.net/galaxy/sendspy", Method.Post);

            AddDefaultHeadersAndCookies(request, _universum);
            request.AddOrUpdateHeader("referer", $"https://{_universum}.ogamex.net/galaxy?x={requestBody.X}&y={requestBody.Y}");
            request.AddBody(requestBody);
            return request;
        }

        private RestRequest GetStartPageRequestConfiguration()
        {
            return new RestRequest();
        }

        private RestRequest GetLoginRequestConfiguration()
        {
            return new RestRequest();
        }

        private RestRequest GetSolarSystemRequestConfiguration()
        {
            var request = new RestRequest("https://" + _universum + ".ogamex.net/galaxy/galaxydata", Method.Get);

            AddDefaultHeadersAndCookies(request, _universum);


            return request;
        }

        private void AddDefaultHeadersAndCookies(RestRequest request, string universum)
        {
            request.AddHeader("authority", $"{universum}.ogamex.net");
            request.AddHeader("accept", "*/*");
            request.AddHeader("accept-language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
            request.AddHeader("referer", $"https://{universum}.ogamex.net/galaxy");
            request.AddHeader("Accept-Encoding", "gzip, deflate");
            request.AddHeader("Connection", "keep-alive");
            request.AddHeader("sec-ch-ua", " \"Google Chrome\";v=\"117\", \"Not; A = Brand\";v=\"8\", \"Chromium\";v=\"117\"");
            request.AddHeader("sec-ch-ua-mobile", "?0");
            request.AddHeader("sec-ch-ua-platform", "\"Windows\"");
            request.AddHeader("sec-fetch-dest", "empty");
            request.AddHeader("sec-fetch-mode", "cors");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36");
            request.AddHeader("x-requested-with", "XMLHttpRequest");

            var token = new Token(GameType.OgameX, universum).GetToken();
            var sessionId = new Token(GameType.OgameX, universum).GetSessionId();

            request.AddCookie("lang", "pl", "/", "tron.ogamex.net");
            request.AddCookie("timeZoneOffset", "120", "/", "tron.ogamex.net");
            request.AddCookie("SessionId", sessionId, "/", "tron.ogamex.net");
            request.AddCookie("gameAuthToken", token, "/", "tron.ogamex.net");
            request.AddCookie("_ga", "GA1.1.1428976385.1681921793", "/", ".ogamex.net");
            //   request.AddCookie("__cf_bm", "0");
            request.AddCookie("_ga_EB8YK2X9FG", "GS1.1.1695987173.382.1.1695987237.0.0.0", "/", ".ogamex.net");
        }
    }
}