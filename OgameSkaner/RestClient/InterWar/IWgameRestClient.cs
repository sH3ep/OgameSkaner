using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using OgameSkaner.Model;
using OgameSkaner.Utils;
using RestSharp;

namespace OgameSkaner.RestClient.InterWar
{
    public class IWgameRestClient : IGameRestClient
    {
        public IWgameRestClient()
        {
            _client = new RestSharp.RestClient("http://www.inter-war.com.pl");
            _requestConfigurator = new InterWarRequestConfigurator();
        }

        #region fields

        private readonly RestSharp.RestClient _client;
        private readonly InterWarRequestConfigurator _requestConfigurator;

        #endregion

        #region PublicMethods

        public string GetMainPage()
        {
            var request = _requestConfigurator.Configure(RequestType.StartPage);

            var response = _client.Execute(request);
            var content = response.Content;
            SaveIntoLogFile(content);

            return content;
        }

        public string LoginToSgame(string login, SecureString password)
        {
            var request = _requestConfigurator.Configure(RequestType.Login);

            request.AddParameter("uni", "1");
            request.AddParameter("username", login);
            request.AddParameter("password", SecureStringToString(password));
            request.AddParameter("submit.x", "57");
            request.AddParameter("submit.y", "12");

            var response = _client.Execute(request);
            var solarSystemPage = response.Content;
            var erorMessage = response.ErrorMessage;
            if (erorMessage != null)
            {
                throw new RestException("Login Error");
            }

            if (CheckLogInStatus() == LoginStatus.LoggedIn)
            {
                MessageBox.Show("Logged In");
            }
            else
            {
                MessageBox.Show("Logging in failed");
            }
            return solarSystemPage;
        }


        public string GetSolarSystem(int galaxy, int solarSystem)
        {
            var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);

            request.AddQueryParameter("page", "galaxy");
            request.AddQueryParameter("mode", "1");

            request.AddParameter("galaxy", galaxy);
            request.AddParameter("system", solarSystem);

            var response = _client.Execute(request);
            var solarSystemPage = response.Content;
            if (!IsResponseCorrect(response))
            {
                SaveIntoLogFile(solarSystemPage);
                throw new RestException("Problem with download Data, check token or LogIn again");
            }

            return solarSystemPage;
        }

        public async Task<string> GetSolarSystemAsync(int galaxy, int solarSystem, ProgresBarData pBData)
        {
            var solarSystemPage = "";
            await Task.Run(() =>
            {
                var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);

                request.AddQueryParameter("page", "galaxy");

                request.AddParameter("galaxy", galaxy);
                request.AddParameter("system", solarSystem);

                var response = _client.Execute(request);
                solarSystemPage = response.Content;
                if (!IsResponseCorrect(response))
                {
                    SaveIntoLogFile(solarSystemPage);
                    throw new RestException("Problem with download Data, check token or LogIn again");
                }

                lock (pBData)
                {
                    pBData.ActualValue++;
                }
            });
            return solarSystemPage;
        }

        public LoginStatus CheckLogInStatus()
        {
            var request = _requestConfigurator.Configure(RequestType.StartPage);
            var response = _client.Execute(request);
            if (IsClientLoggedIn(response)) return LoginStatus.LoggedIn;
            return LoginStatus.LoggedOut;
        }


        public void SpyPlanet(UserPlanet userPlanet,PlanetType planetType)
        {
            var request = _requestConfigurator.Configure(RequestType.SpyPlanet);
            request.Resource = "http://www.inter-war.com.pl/";
            request.AddParameter("mission", "6");
            request.AddParameter("galaxy", userPlanet.Galaxy);
            request.AddParameter("system", userPlanet.SolarSystem);
            request.AddParameter("planet", userPlanet.Position);
            int planetTypeNumber =(int) planetType;
            request.AddParameter("planettype", planetTypeNumber.ToString());
            request.AddParameter("ships", "1");

            var response = _client.Execute(request);
            SaveIntoLogFile(response.Content);
            if (IsSpyResponseCorrect(response)) return;

            throw new RestException("There was an error during Spy request");
        }

        #endregion

        #region PrivateMethods


        private bool IsClientLoggedIn(IRestResponse response)
        {
            try
            {
                var test = response.Content;
                if (test.Contains("<title>Inter-War - Prywatny serwer gry Ogame typu Ugamela i Xnova.</title>") || test.Contains("!DOCTYPE html PUBLIC") || test == "")
                    return false;
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private bool IsResponseCorrect(IRestResponse response)
        {
            try
            {
                var test = response.Headers.First(x => x.Name.ToLower() == "content-length").ToString();
                test = Regex.Match(test, @"\d+").Value;
                if (int.Parse(test) < 1500) return false;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return true;
        }

        private bool IsSpyResponseCorrect(IRestResponse response)
        {
            try
            {
                var test = response.Headers.First(x => x.Name.ToLower() == "content-length").ToString();
                test = Regex.Match(test, @"\d+").Value;
                var contentLength = int.Parse(test);
                if (contentLength < 300 && contentLength > 60)
                    if (response.Content.Contains("Sonda Szpiegowska"))
                        return true;
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string SecureStringToString(SecureString value)
        {
            if (value != null)
            {
                var bstr = Marshal.SecureStringToBSTR(value);
                try
                {

                    return Marshal.PtrToStringBSTR(bstr);
                }
                finally
                {
                    Marshal.FreeBSTR(bstr);
                }
            }
            else
            {
                MessageBox.Show("Wrong Password");
                return "";
            }
        }

        private void SaveIntoLogFile(string text)
        {
            var path = "request_log" + ".txt";

            // Create a file to write to.
            using (var sw = File.AppendText(path))
            {
                sw.WriteLine("");
                sw.WriteLine("----------------------------------------------" + DateTime.Now +
                             "--------------------------------------");

                sw.WriteLine(text);

                sw.Close();
            }
        }

        public void SpyPlanet(UserPlanet userPlanet)
        {
            throw new NotImplementedException();
        }

        public void SpyPlanet(UserPlanet userPlanet, UserPlanet planetType)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}

