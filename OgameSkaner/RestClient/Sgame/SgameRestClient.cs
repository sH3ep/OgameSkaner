﻿using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using OgameSkaner.RestClient.InterWar;
using OgameSkaner.Utils;
using RestSharp;

namespace OgameSkaner.RestClient
{
  

    public class SgameRestClient : IGameRestClient
    {
        public SgameRestClient(int universum)
        {
            _client = new RestSharp.RestClient("https://uni"+universum+".sgame.pl");
            _requestConfigurator = new RequestConfigurator(universum);
            _universum = universum;
        }

        #region fields
        private int _universum;
        private readonly RestSharp.RestClient _client;
        private readonly RequestConfigurator _requestConfigurator;

        #endregion

        #region PublicMethods

        public GameType GetGameType()
        {
            return GameType.Sgame;
        }
        public int GetUniversum()
        {
            return _universum;
        }

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

            request.AddParameter("uni", "2");
            request.AddParameter("username", login);
            request.AddParameter("password", SecureStringToString(password));

            var response = _client.Execute(request);
            var solarSystemPage = response.Content;
            var erorMessage = response.ErrorMessage;
            if (erorMessage != null)
            {
                SaveIntoLogFile(solarSystemPage + Environment.NewLine +
                                "------------------------------Error Message Below----------------------------" +
                                Environment.NewLine + erorMessage);
                throw new RestException("Login Error");
            }

            MessageBox.Show("Login successful");
            return solarSystemPage;
        }


        public string GetSolarSystem(int galaxy, int solarSystem)
        {
            var request = _requestConfigurator.Configure(RequestType.GetSolarSystem);

            request.AddQueryParameter("page", "galaxy");

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


        public void SpyPlanet(UserPlanet userPlanet)
        {
            var request = _requestConfigurator.Configure(RequestType.SpyPlanet);
            request.Resource = "https://uni2.sgame.pl/game.php?page=fleetAjax&ajax=1&mission=6&planetID=" +
                               userPlanet.PlanetId;
            AddSpecialSpyParameters(request, userPlanet);

            var response = _client.Execute(request);
            SaveIntoLogFile(response.Content);
            if (IsSpyResponseCorrect(response)) return;

            throw new RestException("There was an error during Spy request");
        }

        public void SpyPlanet(UserPlanet userPlanet,PlanetType planetType)
        {
            var request = _requestConfigurator.Configure(RequestType.SpyPlanet);
            request.Resource = "https://uni2.sgame.pl/game.php?page=fleetAjax&ajax=1&mission=6&planetID=" +
                               userPlanet.PlanetId;
            AddSpecialSpyParameters(request, userPlanet);

            var response = _client.Execute(request);
            SaveIntoLogFile(response.Content);
            if (IsSpyResponseCorrect(response)) return;

            throw new RestException("There was an error during Spy request");
        }

        #endregion

        #region PrivateMethods

        private void AddSpecialSpyParameters(RestRequest request, UserPlanet userPlanet)
        {
            request.AddHeader("path", "/game.php?page=fleetAjax&ajax=1&mission=6&planetID=" + userPlanet.PlanetId);

            request.AddQueryParameter("PlanetId", userPlanet.PlanetId.ToString());
        }

        private bool IsClientLoggedIn(IRestResponse response)
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

