using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using OgameSkaner.Model;
using RestSharp;

namespace OgameSkaner.RestClient
{
    public class SgameRestClient
    {

        public SgameRestClient()
        {
            _client = new RestSharp.RestClient("https://uni2.sgame.pl");
            _requestConfigurator = new RequestConfigurator();
        }

        #region fields

        private readonly RestSharp.RestClient _client;
        private readonly RequestConfigurator _requestConfigurator;

        #endregion

        #region PublicMethods



        public string GetMainPage()
        {
            var request = _requestConfigurator.Configure(RequestType.StartPage);

            var response = _client.Execute(request);
            var content = response.Content;
            saveContent(content);

            return content;
        }

        public string LoginToSgame(string login,SecureString password)
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
                saveContent(solarSystemPage + Environment.NewLine +
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
                saveContent(solarSystemPage);
                throw new RestException("Problem with download Data, check token or LogIn again");
            }

            return solarSystemPage;
        }

        public LoginStatus CheckLogInStatus()
        {
            var request = _requestConfigurator.Configure(RequestType.StartPage);
            var response = _client.Execute(request);
            if (IsClientLoggedIn(response))
            {
                return LoginStatus.LoggedIn;
            }
            return LoginStatus.LoggedOut;
        }

        #endregion

        #region PrivateMethods

        private bool IsClientLoggedIn(IRestResponse response)
        {
            try
            {
                var test = response.Headers.First(x => x.Name.ToLower() == "content-length").ToString();
                test = Regex.Match(test, @"\d+").Value;
                if (int.Parse(test) < 1500)
                {
                    return false;
                }
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
                if (int.Parse(test)<1500 )
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }





            return true;
        }
         
        private string SecureStringToString(SecureString value)
        {
            IntPtr bstr = Marshal.SecureStringToBSTR(value);

            try
            {
                return Marshal.PtrToStringBSTR(bstr);
            }
            finally
            {
                Marshal.FreeBSTR(bstr);
            }
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

        #endregion

    }
}