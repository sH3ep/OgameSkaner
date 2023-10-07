using OgameSkaner.Model;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.RestClient.OgameX;
using OgameSkaner.Utils;
using RestSharp;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OgameSkaner.RestClient
{


    public class OgameXRestClient : IGameRestClient
    {
        public OgameXRestClient(string universum)
        {
            _client = new RestSharp.RestClient("https://" + universum + ".ogamex.net");
            _requestConfigurator = new OgamexRequestConfigurator(universum);
            SetSpecialNumber();
            _universum = universum;
        }

        #region fields
        private string _universum;
        private readonly RestSharp.RestClient _client;
        private readonly OgamexRequestConfigurator _requestConfigurator;
        private long _specialNumber;

        #endregion

        #region PublicMethods

        public GameType GetGameType()
        {
            return GameType.OgameX;
        }
        public string GetUniversum()
        {
            return _universum;
        }

        public void SetSpecialNumber()
        {
            var newSpecialNumber = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            if (DateTimeOffset.FromUnixTimeMilliseconds(newSpecialNumber).AddMinutes(-5) > DateTimeOffset.FromUnixTimeMilliseconds(_specialNumber))
            {
                _specialNumber = newSpecialNumber;
            }
        }

        public string GetMainPage()
        {
            throw new NotImplementedException();

            //var request = _requestConfigurator.Configure(RequestType.StartPage);

            //var response = _client.Execute(request);
            //var content = response.Content;
            //SaveIntoLogFile(content);

            //return content;
        }

        public string LoginToGame(string login, SecureString password)
        {
            throw new NotImplementedException();

            //var request = _requestConfigurator.Configure(RequestType.Login);

            //request.AddParameter("uni", "2");
            //request.AddParameter("username", login);
            //request.AddParameter("password", SecureStringToString(password));

            //var response = _client.Execute(request);
            //var solarSystemPage = response.Content;
            //var erorMessage = response.ErrorMessage;
            //if (erorMessage != null)
            //{
            //    SaveIntoLogFile(solarSystemPage + Environment.NewLine +
            //                    "------------------------------Error Message Below----------------------------" +
            //                    Environment.NewLine + erorMessage);
            //    throw new RestException("Login Error");
            //}

            //MessageBox.Show("Login successful");
            //return solarSystemPage;
        }


        public string GetSolarSystem(int galaxy, int solarSystem)
        {
            SetSpecialNumber();
            var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);
            _specialNumber++;
            request.AddQueryParameter("x", galaxy.ToString());
            request.AddQueryParameter("y", solarSystem.ToString());
            request.AddQueryParameter("_", _specialNumber.ToString());


            var response = _client.Execute(request);

            var solarSystemPage = response.Content;

            return solarSystemPage;
        }

        public async Task<string> GetSolarSystemAsync(int galaxy, int solarSystem, ProgresBarData pBData)
        {
            throw new NotImplementedException();

            //var solarSystemPage = "";
            //await Task.Run(() =>
            //{
            //    var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);

            //    request.AddQueryParameter("page", "galaxy");

            //    request.AddParameter("galaxy", galaxy);
            //    request.AddParameter("system", solarSystem);

            //    var response = _client.Execute(request);
            //    solarSystemPage = response.Content;
            //    if (!IsResponseCorrect(response))
            //    {
            //        SaveIntoLogFile(solarSystemPage);
            //        throw new RestException("Problem with download Data, check token or LogIn again");
            //    }

            //    lock (pBData)
            //    {
            //        pBData.ActualValue++;
            //    }
            //});
            //return solarSystemPage;
        }

        public LoginStatus CheckLogInStatus()
        {
            throw new NotImplementedException();

            //var request = _requestConfigurator.Configure(RequestType.StartPage);
            //var response = _client.Execute(request);
            //if (IsClientLoggedIn(response)) return LoginStatus.LoggedIn;
            //return LoginStatus.LoggedOut;
        }


        public void SpyPlanet(UserPlanet userPlanet)
        {
            throw new NotImplementedException();

            //var request = _requestConfigurator.Configure(RequestType.SpyPlanet);
            //request.Resource = "https://uni2.sgame.pl/game.php?page=fleetAjax&ajax=1&mission=6&planetID=" +
            //                   userPlanet.PlanetId;
            //AddSpecialSpyParameters(request, userPlanet);

            //var response = _client.Execute(request);
            //SaveIntoLogFile(response.Content);
            //if (IsSpyResponseCorrect(response)) return;

            //throw new RestException("There was an error during Spy request");
        }

        public void SpyPlanet(UserPlanet userPlanet, PlanetType planetType)
        {
            var body = new SpyPlanetRequest(userPlanet.Galaxy, userPlanet.SolarSystem, userPlanet.Position, planetType == PlanetType.MOON);
            var request = _requestConfigurator.GetSpyReportReqestConfiguration(body);

           var response = _client.Execute(request);
        }

        #endregion

        #region PrivateMethods

        private bool IsClientLoggedIn(RestResponse response)
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
                return false;
            }

            return true;
        }

        private bool IsResponseCorrect(RestResponse response)
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

        private bool IsSpyResponseCorrect(RestResponse response)
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

        public void SpyPlanet(UserPlanet userPlanet, UserPlanet planetType)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}

