
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using RestSharp;

namespace OgameSkaner.RestClient
{
    public class SgameRestClient
    {
        private RestSharp.RestClient _client;


        public SgameRestClient()
        {
            _client = new RestSharp.RestClient("https://uni2.sgame.pl");
        }

        public void GetMainPage()
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
            //request.AddHeader("cookie", "lang=pl; scroll=0; 2Moons=6qa24isbmkd4fgj40utvvctbsr");
            request.AddCookie("lang", "pl");
            request.AddCookie("scroll", "0");
            request.AddCookie("2Moons", "6qa24isbmkd4fgj40utvvctbsr");
            request.AddHeader("referer","https://uni2.sgame.pl/game.php?page=overview");
            request.AddHeader("sec-fetch-mode", "navigate");
            request.AddHeader("sec-fetch-site", "same-origin");
            request.AddHeader("sec-fetch-user", "?1");
            request.AddHeader("upgrade-insecure-requests", "1");
            request.AddHeader("user-agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/76.0.3809.100 Safari/537.36");

            request.AddQueryParameter("page", "overview");

            IRestResponse response = _client.Execute(request);
            var content = response.Content;
            saveContent(content);



        }


        public void GetSolarSystemPhpFile(int galaxy, int solarSystem)
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
            request.AddCookie("2Moons", "6qa24isbmkd4fgj40utvvctbsr");

            request.AddQueryParameter("page", "galaxy");
            string temp = "systemRight=dr&galaxy=1&system=1";

          request.AddParameter("galaxy", "1");
          request.AddParameter("system", "3");

          

            IRestResponse response = _client.Execute(request);
            var content = response.Content;
            var erorMessage = response.ErrorMessage;
            saveContent(content+erorMessage);
        }


        private void saveContent(string text)
        {
            string path = "test_resta" + ".txt";

            // Create a file to write to.
            using (StreamWriter sw = File.CreateText(path))
            {
               
                        sw.WriteLine(text);
                    
                sw.Close();
            }
        }

        
    }
}