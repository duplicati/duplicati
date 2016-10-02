//  Copyright (C) 2016, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
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
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Duplicati.Library.Localization
{
    /// <summary>
    /// Class for reading embedded PO files
    /// </summary>
    public class PoLocalizationService : ILocalizationService
    {
        /// <summary>
        /// The environment variable used to locate PO files
        /// </summary>
        public const string LOCALIZATIONDIR_ENVNAME = "LOCALIZATION_FOLDER";

        /// <summary>
        /// A cached copy of all strings
        /// </summary>
        private Dictionary<string, string> m_messages = new Dictionary<string, string>();

        /// <summary>
        /// Path to search for extra .po files in
        /// </summary>
        public static string[] SearchPaths =
            string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string)
                ? new string[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) }
                : (AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string).Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);


        /// <summary>
        /// Assembly to look for .po files in
        /// </summary>
        public static Assembly SearchAssembly = Assembly.GetExecutingAssembly();

        /// <summary>
        /// Regular expression to match a PO line entry
        /// </summary>
        private static readonly Regex PO_MATCHER = new Regex(@"(?<key>\w+(\[(?<index>[0-9]+)\])?)\s+""(?<value>(?:\\.|""""|[^""\\])*)""");

        /// <summary>
        /// Regular expression to match a locale from a filename
        /// </summary>
        private static readonly Regex CI_MATCHER = new Regex(@"localization-(?<culture>" + LocalizationService.CI_MATCHER + @")\.po");

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Localization.PoLocalizationService"/> class.
        /// </summary>
        /// <param name="ci">The culture to find.</param>
        public PoLocalizationService(CultureInfo ci)
        {
            var filenames = new string[] { 
                // Load the generic country version first
                string.Format("localization-{0}.po", ci.Name), 
                // Then the specialized version with overrides
                string.Format("localization-{0}.po", ci.TwoLetterISOLanguageName) 
            };

            foreach(var fn in filenames)
            {
                // Use embedded version first
                if (SearchAssembly != null)
                {
                    // Find the localization streams inside the assembly
                    var names =
                        from name in SearchAssembly.GetManifestResourceNames()
                        let m = CI_MATCHER.Match(name)
                        let c = m.Success && string.Equals(m.Value, fn, StringComparison.InvariantCultureIgnoreCase) ? LocalizationService.ParseCulture(m.Groups["culture"].Value) : null
                        where c != null
                        select name;

                    foreach (var sn in names)
                        using (var s = SearchAssembly.GetManifestResourceStream(sn))
                            if (s != null)
                                using (var sr = new StreamReader(s, System.Text.Encoding.UTF8, true))
                                    ParseStream(sr);
                }

                // Then override with external
                foreach(var sp in SearchPaths)
                    if (!string.IsNullOrWhiteSpace(sp) && File.Exists(Path.Combine(sp, fn)))
                        using (var fs = new StreamReader(Path.Combine(sp, fn), System.Text.Encoding.UTF8, true))
                            ParseStream(fs);
            }
        }

        /// <summary>
        /// Parses a PO file by reading each line
        /// </summary>
        /// <param name="rd">The reader to extract data from.</param>
        private void ParseStream(TextReader rd)
        {
            string ln;
            string msgid = null;

            while ((ln = rd.ReadLine()) != null)
            {
                var m = PO_MATCHER.Match(ln);
                if (m.Success)
                {
                    var key = m.Groups["key"].Value;
                    var value = m.Groups["value"].Value;

                    if (!string.IsNullOrEmpty(value))
                        value = value.Replace("\"\"", "\"").Replace("\\\"", "\"").Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\n", "\n");


                    if (string.Equals(key, "msgid", StringComparison.OrdinalIgnoreCase))
                    {
                        msgid = value;
                    }
                    else if (string.Equals(key, "msgstr", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(value))
                            m_messages[msgid] = value;
                    }
                }
            }
        }

        /// <summary>
        /// Performs the actual translation
        /// </summary>
        /// <param name="msg">The message to translate.</param>
        private string Transform(string msg)
        {
            string res;
            if (string.IsNullOrWhiteSpace(msg) || !m_messages.TryGetValue(msg, out res))
                return msg;
            
            return res;
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <returns>The localized string</returns>
        public string Localize(string message)
        {
            return string.Format(Transform(message));
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <returns>The localized string</returns>
        public string Localize(string message, object arg0)
        {
            return string.Format(Transform(message), arg0);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <returns>The localized string</returns>
        public string Localize(string message, object arg0, object arg1)
        {
            return string.Format(Transform(message), arg0, arg1);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <param name="arg1">The second argument</param>
        /// <param name="arg2">The third argument</param>
        /// <returns>The localized string</returns>
        public string Localize(string message, object arg0, object arg1, object arg2)
        {
            return string.Format(Transform(message), arg0, arg1, arg2);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="args">The arguments</param>
        public string Localize(string message, params object[] args)
        {
            return string.Format(Transform(message), args);
        }

        /// <summary>
        /// Gets all strings.
        /// </summary>
        public IDictionary<string, string> AllStrings { get { return m_messages; } }

        private static string[] m_supportedcultures = null;

        /// <summary>
        /// Gets a list of all the supported cultures
        /// </summary>
        public static IEnumerable<string> SupportedCultures
        {
            get
            {

                if (m_supportedcultures == null)
                {
                    var lst = new string[0].AsEnumerable();

                    if (SearchAssembly != null)
                        lst = lst.Union(SearchAssembly.GetManifestResourceNames());
                    foreach (var sp in SearchPaths)
                        if (Directory.Exists(sp))
                            lst = lst.Union(Directory.GetFiles(sp, "localization-*.po"));

                    var allcultures =
                        from name in lst
                        let m = CI_MATCHER.Match(name)
                        let ci = m.Success ? LocalizationService.ParseCulture(m.Groups["culture"].Value) : null
                        where ci != null
                        select ci;

                    m_supportedcultures = 
                        allcultures
                            .Select(x => x.TwoLetterISOLanguageName).Distinct().OrderBy(x => x)
                            .Union(
                                allcultures.Select(x => x.Name).Distinct().OrderBy(x => x)
                        ).ToArray();
                        
                }

                return m_supportedcultures;
            }
        }
    }
}
