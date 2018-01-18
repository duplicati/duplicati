using System;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using Duplicati.Library.Backend.GoogleServices;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;

namespace Duplicati.Library.Backend.GoogleDrive
{
    public class GoogleOAuthHelper : OAuthHelper
    {
        GoogleOAuth _googleOAuth;

        public GoogleOAuthHelper ()
        { }

        public override HttpWebRequest CreateRequest(string url, string method = null)
        {
            //Utility to call base of a base class!!!!!!
            //---------------------------------------------
            var ptr = typeof(JSONWebHelper).GetMethod("CreateRequest").MethodHandle.GetFunctionPointer();
            var baseMethod = (Func<string,string,HttpWebRequest>)Activator.CreateInstance(typeof(Func<string, string, HttpWebRequest>), this, ptr);
            var r = baseMethod(url, method);

            r.Headers["Authorization"] = string.Format("Bearer {0}", AccessToken);
            return r;
        }

        public string AccessToken
        {
            get
            {
                if (_googleOAuth == null)
                    _googleOAuth = new GoogleOAuth();

                return _googleOAuth.GetToken();
            }
        }
    }

    public class GoogleOAuth
    {
        // client configuration
        const string clientID = "YOUR CLIENT ID";
        const string clientSecret = "YOUR CLIENT SECRET";
        const string authorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        const string tokenEndpoint = "https://www.googleapis.com/oauth2/v4/token";
        const string userInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
        const string scope= "openid https://www.googleapis.com/auth/drive";

        TokenStatus _tokenStatus=null;
        double tokenDuration = 3540;

        string filename =Directory.GetCurrentDirectory() + @"\googleOauthconfig.json";
        public string GetToken()
        {
            if (_tokenStatus!=null && _tokenStatus.IsValid())
                return _tokenStatus.AccessToken;

            TokenStatus tempTokenStatus = null;
            try
            {
                if (File.Exists(filename))
                    tempTokenStatus = JsonConvert.DeserializeObject<TokenStatus>(File.ReadAllText(filename));
            }
            catch(Exception ex)
            { }

            //Check if file exist
            if (tempTokenStatus != null)
            {
                //Check if token still valid
                _tokenStatus = tempTokenStatus;
                if (_tokenStatus.IsValid())
                {
                    //userinfoCall(_tokenStatus.AccessToken).Wait();
                    return _tokenStatus.AccessToken;
                }

                //Get refresk token
                var t = GoogleOAuthRefresh(_tokenStatus.RefreshToken).Result;
                if (t.Item1)
                {
                    _tokenStatus = new TokenStatus(t.Item2, _tokenStatus.RefreshToken, DateTime.Now + TimeSpan.FromSeconds(tokenDuration));
                    File.WriteAllText(filename, JsonConvert.SerializeObject(_tokenStatus));
                    return _tokenStatus.AccessToken;
                }
                else
                    throw (new Exception("GoogleOAuthRefresh() Error. Error details: " + t.Item2));
            }
            else
            {
                //Get new token
                var t = GoogleOAuthFlow().Result;
                if (t.Item1)
                {
                    _tokenStatus = new TokenStatus(t.Item2, t.Item3, DateTime.Now + TimeSpan.FromSeconds(tokenDuration));
                    File.WriteAllText(filename, JsonConvert.SerializeObject(_tokenStatus));
                    return _tokenStatus.AccessToken;
                }
                else
                    throw (new Exception("GoogleOAuthFlow() Error. Error details: " + t.Item2));
            }
        }

        // ref http://stackoverflow.com/a/3978040
        private static int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<Tuple<bool,string,string>> GoogleOAuthFlow()
        {
            // Generates state and PKCE values.
            string state = randomDataBase64url(32);
            string code_verifier = randomDataBase64url(32);
            string code_challenge = base64urlencodeNoPadding(sha256(code_verifier));
            const string code_challenge_method = "S256";

            // Creates a redirect URI using an available port on the loopback address.
            string redirectURI = string.Format("http://{0}:{1}/", IPAddress.Loopback, GetRandomUnusedPort());
            //output("redirect URI: " + redirectURI);

            // Creates an HttpListener to listen for requests on that redirect URI.
            var http = new HttpListener();
            http.Prefixes.Add(redirectURI);
            //output("Listening..");
            http.Start();

            // Creates the OAuth 2.0 authorization request.
            string authorizationRequest = string.Format("{0}?response_type=code&scope={6}%20profile&redirect_uri={1}&client_id={2}&state={3}&code_challenge={4}&code_challenge_method={5}",
                authorizationEndpoint,
                System.Uri.EscapeDataString(redirectURI),
                clientID,
                state,
                code_challenge,
                code_challenge_method,
                scope);

            // Opens request in the browser.
            System.Diagnostics.Process.Start(authorizationRequest);

            // Waits for the OAuth authorization response.
            var context = await http.GetContextAsync();

            // Brings this app back to the foreground.
            //this.Activate();

            // Sends an HTTP response to the browser.
            var response = context.Response;
            string responseString = string.Format("<html><head><meta http-equiv='refresh' content='10;url=https://google.com'></head><body>Please return to the app.</body></html>");
            var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            var responseOutput = response.OutputStream;
            Task responseTask = responseOutput.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
            {
                responseOutput.Close();
                http.Stop();
                Console.WriteLine("HTTP server stopped.");
            });

            // Checks for errors.
            if (context.Request.QueryString.Get("error") != null)
            {
                var err = String.Format("OAuth authorization error: {0}.", context.Request.QueryString.Get("error"));
                output(err);
                return new Tuple<bool, string,string>(false,err,"");
            }
            if (context.Request.QueryString.Get("code") == null
                || context.Request.QueryString.Get("state") == null)
            {
                var err = "Malformed authorization response. " + context.Request.QueryString;
                output(err);
                return new Tuple<bool, string,string>(false, err,"");
            }

            // extracts the code
            var code = context.Request.QueryString.Get("code");
            var incoming_state = context.Request.QueryString.Get("state");

            // Compares the receieved state to the expected value, to ensure that
            // this app made the request which resulted in authorization.
            if (incoming_state != state)
            {
                var err = String.Format("Received request with invalid state ({0})", incoming_state);
                output(err);
                return new Tuple<bool, string,string>(false, err,"");
            }
            //output("Authorization code: " + code);

            // Starts the code exchange at the Token Endpoint.
            return await performCodeExchange(code,"", code_verifier, redirectURI);
        }

        private async Task<Tuple<bool, string,string>> GoogleOAuthRefresh(string refresh_token)
        {
            // Creates a redirect URI using an available port on the loopback address.
            string redirectURI = string.Format("http://{0}:{1}/", IPAddress.Loopback, GetRandomUnusedPort());
            //output("redirect URI: " + redirectURI);

            //Get refresh token
            return await performCodeExchange("", refresh_token, "", redirectURI);
        }

        async Task<Tuple<bool,string,string>> performCodeExchange(string code,string refresh_token, string code_verifier, string redirectURI)
        {
            //output("Exchanging code for tokens...");

            // builds the  request
            string tokenRequestURI = "https://www.googleapis.com/oauth2/v4/token";
            string tokenRequestBody = code != "" ?
                string.Format("code={0}&redirect_uri={1}&client_id={2}&code_verifier={3}&client_secret={4}&scope=&grant_type=authorization_code",
                code,
                System.Uri.EscapeDataString(redirectURI),
                clientID,
                code_verifier,
                clientSecret
                ) :
                string.Format("refresh_token={0}&redirect_uri={1}&client_id={2}&client_secret={4}&scope=&grant_type=refresh_token",
                refresh_token,
                System.Uri.EscapeDataString(redirectURI),
                clientID,
                code_verifier,
                clientSecret
                );


            // sends the request
            HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(tokenRequestURI);
            tokenRequest.Method = "POST";
            tokenRequest.ContentType = "application/x-www-form-urlencoded";
            tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            byte[] _byteVersion = Encoding.ASCII.GetBytes(tokenRequestBody);
            tokenRequest.ContentLength = _byteVersion.Length;
            Stream stream = tokenRequest.GetRequestStream();
            await stream.WriteAsync(_byteVersion, 0, _byteVersion.Length);
            stream.Close();

            try
            {
                // gets the response
                WebResponse tokenResponse = await tokenRequest.GetResponseAsync();
                using (StreamReader reader = new StreamReader(tokenResponse.GetResponseStream()))
                {
                    // reads response body
                    string responseText = await reader.ReadToEndAsync();
                    output(responseText);

                    // converts to dictionary
                    Dictionary<string, string> tokenEndpointDecoded = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);

                    string access_token = tokenEndpointDecoded["access_token"];
                    var refresh_token_new = "";
                    if (code!="")
                        refresh_token_new = tokenEndpointDecoded["refresh_token"];
                    return new Tuple<bool, string,string>(true, access_token, refresh_token_new);
                }
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError)
                {
                    var response = ex.Response as HttpWebResponse;
                    if (response != null)
                    {
                        output("HTTP: " + response.StatusCode);
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            // reads response body
                            string responseText = await reader.ReadToEndAsync();
                            output(responseText);

                            return new Tuple<bool, string,string>(false, responseText,"");
                        }
                    }
                    else
                        return new Tuple<bool, string,string>(false, "Unknow error....","");
                }
                else
                    return new Tuple<bool, string,string>(false, "Unknow error....","");
            }
        }


        public async Task userinfoCall(string access_token)
        {
            output("Making API Call to Userinfo...");

            // builds the  request
            string userinfoRequestURI = "https://www.googleapis.com/oauth2/v3/userinfo";

            // sends the request
            HttpWebRequest userinfoRequest = (HttpWebRequest)WebRequest.Create(userinfoRequestURI);
            userinfoRequest.Method = "GET";
            userinfoRequest.Headers.Add(string.Format("Authorization: Bearer {0}", access_token));
            userinfoRequest.ContentType = "application/x-www-form-urlencoded";
            userinfoRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

            // gets the response
            WebResponse userinfoResponse = await userinfoRequest.GetResponseAsync();
            using (StreamReader userinfoResponseReader = new StreamReader(userinfoResponse.GetResponseStream()))
            {
                // reads response body
                string userinfoResponseText = await userinfoResponseReader.ReadToEndAsync();
                output(userinfoResponseText);
            }
        }

        /// <summary>
        /// Appends the given string to the on-screen log, and the debug console.
        /// </summary>
        /// <param name="output">string to be appended</param>
        void output(string output)
        {
            Console.WriteLine(output);
        }

        /// <summary>
        /// Returns URI-safe data with a given input length.
        /// </summary>
        /// <param name="length">Input length (nb. output will be longer)</param>
        /// <returns></returns>
        static string randomDataBase64url(uint length)
        {
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
            byte[] bytes = new byte[length];
            rng.GetBytes(bytes);
            return base64urlencodeNoPadding(bytes);
        }

        /// <summary>
        /// Returns the SHA256 hash of the input string.
        /// </summary>
        /// <param name="inputStirng"></param>
        /// <returns></returns>
        static byte[] sha256(string inputStirng)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(inputStirng);
            SHA256Managed sha256 = new SHA256Managed();
            return sha256.ComputeHash(bytes);
        }

        /// <summary>
        /// Base64url no-padding encodes the given input buffer.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        static string base64urlencodeNoPadding(byte[] buffer)
        {
            string base64 = Convert.ToBase64String(buffer);

            // Converts base64 to base64url.
            base64 = base64.Replace("+", "-");
            base64 = base64.Replace("/", "_");
            // Strips padding.
            base64 = base64.Replace("=", "");

            return base64;
        }
    }

    public class TokenStatus
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpirationDate { get; set; }

        public TokenStatus(string accessToken,string refreshToken, DateTime expirationDate)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            ExpirationDate = expirationDate;
        }

        public bool IsValid()
        {
            return DateTime.Now < ExpirationDate;
        }
    }
}
