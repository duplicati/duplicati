//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;

namespace Duplicati.Library.AutoUpdater
{
    public static class AutoUpdateSettings
    {
        private static Dictionary<string, string> _cache = new Dictionary<string, string>();
        private const string APP_NAME = "AutoUpdateAppName.txt";
        private const string UPDATE_URL = "AutoUpdateURL.txt";
        private const string UPDATE_KEY = "AutoUpdateSignKey.txt";
        private const string UPDATE_README = "AutoUpdateFolderReadme.txt";

        private const string UPDATEURL_ENVNAME_TEMPLATE = "AUTOUPDATER_{0}_URLS";


        static AutoUpdateSettings()
        {
            ReadResourceText(APP_NAME);
            ReadResourceText(UPDATE_URL);
            ReadResourceText(UPDATE_KEY);
            ReadResourceText(UPDATE_README);
        }

        private static string ReadResourceText(string name)
        {
            string result;
            if (_cache.TryGetValue(name, out result))
                return result;

            try
            {
                using (var rd = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(AutoUpdateSettings), name)))
                    result = rd.ReadToEnd();
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(result))
                result = "";
            else
                result = result.Trim();

            _cache[name] = result;
            return result;
        }

        public static string[] URLs
        {
            get 
            { 
                if (UsesAlternateURLs)
                    return Environment.GetEnvironmentVariable(string.Format(UPDATEURL_ENVNAME_TEMPLATE, AppName)).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                else
                    return ReadResourceText(UPDATE_URL).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);; 
            }
        }

        public static bool UsesAlternateURLs
        {
            get 
            {
                return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(string.Format(UPDATEURL_ENVNAME_TEMPLATE, AppName)));
            }
        }


        public static string AppName
        {
            get { return ReadResourceText(APP_NAME); }
        }

        public static string UpdateFolderReadme
        {
            get { return ReadResourceText(UPDATE_README); }
        }

        public static System.Security.Cryptography.RSACryptoServiceProvider SignKey
        {
            get 
            { 
                try
                {
                    var key = System.Security.Cryptography.RSACryptoServiceProvider.Create();
                    key.FromXmlString(ReadResourceText(UPDATE_KEY)); 
                    return (System.Security.Cryptography.RSACryptoServiceProvider)key;
                }
                catch
                {
                }

                return null;
            }
        }
    }
}

