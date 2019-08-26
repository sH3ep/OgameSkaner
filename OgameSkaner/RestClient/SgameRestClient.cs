using System.IO;
using OgameSkaner.Model;

namespace OgameSkaner.RestClient
{
    public class SgameRestClient
    {
        private readonly RestSharp.RestClient _client;
        private readonly RequestConfigurator _requestConfigurator;


        public SgameRestClient()
        {
            _client = new RestSharp.RestClient("https://uni2.sgame.pl");
            _requestConfigurator = new RequestConfigurator();
        }

        public void GetMainPage()
        {
            var request = _requestConfigurator.Configure(RequestType.StartPage);

            var response = _client.Execute(request);
            var content = response.Content;
            saveContent(content);
        }


        public string GetSolarSystem(int galaxy, int solarSystem)
        {
            var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);


            request.AddQueryParameter("page", "galaxy");

            request.AddParameter("galaxy", galaxy);
            request.AddParameter("system", solarSystem);


            var response = _client.Execute(request);
            var solarSystemPage = response.Content;
            var erorMessage = response.ErrorMessage;
            saveContent(solarSystemPage + erorMessage);
            return solarSystemPage;
        }


        private void saveContent(string text)
        {
            var path = "request_log" + ".txt";

            // Create a file to write to.
            using (var sw = File.CreateText(path))
            {
                sw.WriteLine(text);

                sw.Close();
            }
        }
    }
}