using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend.Storx
{
    public class Access
    {
        private const string root = "https://storx.io/api/";
        private const string key = "eyB6K9tfCOxxas7hkFDcZ4ulcgEsJ80V";
        private const string vi = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string salttext = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public class AccessModel
        {
            [JsonProperty("token")]
            public string token { get; set; }
            [JsonProperty("user")]
            public userAccessModel user { get; set; }
        }

        public class userAccessModel
        {
            [JsonProperty("mnemonic")]
            public string mnemonic { get; set; }
            [JsonProperty("root_folder_id")]
            public string root_folder_id { get; set; }
        }

        public AccessModel accessApi(string email, string password, string sKey)
        {
            string endpoint = "access_core";
            var fl = new AccessModel();
            string encPassword=Encrypt(password);
            try
            {
                System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(root + endpoint);
                req.Accept = string.Format("*/*");
                req.KeepAlive = false;
                req.UserAgent = string.Format("Storx-Agent (Duplicati Storx client {0})", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
                req.Method = System.Net.WebRequestMethods.Http.Post;
                string postData = "email=" + email + "";
                postData += "&password=" + encPassword + "";
                postData += "&sKey=" + sKey + "";
                byte[] byteArray = Encoding.UTF8.GetBytes(postData);
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = byteArray.Length;
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (var rs = areq.GetRequestStream())
                    rs.Write(byteArray, 0, byteArray.Length);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    Stream dataStream = resp.GetResponseStream();
                    StreamReader reader = new StreamReader(dataStream);
                    var responseFromServer = reader;
                    var serializer = new JsonSerializer();
                    var jr1 = new Newtonsoft.Json.JsonTextReader(responseFromServer);
                    fl = (AccessModel)serializer.Deserialize(jr1, typeof(AccessModel));
                    return fl;
                }
            }
            catch (System.Net.WebException wex)
            {
                throw new Exception(getResponseBodyOnError(endpoint, wex));
            }
        }

        private RijndaelManaged myRijndael = new RijndaelManaged();
        private int iterations;
        private byte[] salt;

        public string Encrypt(string strPlainText)
        {
            myRijndael.BlockSize = 128;
            myRijndael.KeySize = 128;
            myRijndael.IV = HexStringToByteArray(string.Format(vi));
            myRijndael.Padding = PaddingMode.PKCS7;
            myRijndael.Mode = CipherMode.CBC;
            iterations = 1000;
            salt = System.Text.Encoding.UTF8.GetBytes(string.Format(salttext));
            myRijndael.Key = GenerateKey(string.Format(key));
            byte[] strText = new System.Text.UTF8Encoding().GetBytes(strPlainText);
            ICryptoTransform transform = myRijndael.CreateEncryptor();
            byte[] cipherText = transform.TransformFinalBlock(strText, 0, strText.Length);

            return BitConverter.ToString(cipherText).Replace("-", "");
        }

        public static byte[] HexStringToByteArray(string strHex)
        {
            dynamic r = new byte[strHex.Length / 2];
            for (int i = 0; i <= strHex.Length - 1; i += 2)
            {
                r[i / 2] = Convert.ToByte(Convert.ToInt32(strHex.Substring(i, 2), 16));
            }
            return r;
        }

        private byte[] GenerateKey(string strPassword)
        {
            Rfc2898DeriveBytes rfc2898 = new Rfc2898DeriveBytes(System.Text.Encoding.UTF8.GetBytes(strPassword), salt, iterations);

            return rfc2898.GetBytes(128 / 8);
        }
        private string getResponseBodyOnError(string context, System.Net.WebException wex)
        {
            HttpWebResponse response = wex.Response as HttpWebResponse;
            if (response is null)
            {
                return $"{context} failed with error: {wex.Message}";
            }

            string body = "";
            using (System.IO.Stream data = response.GetResponseStream())
            using (var reader = new System.IO.StreamReader(data))
            {
                body = reader.ReadToEnd();
            }
            return string.Format("{0} failed, response: {1}", context, body);
        }
    }
}
