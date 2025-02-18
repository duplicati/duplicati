// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NGettext;

namespace Duplicati.Library.Localization
{
    /// <summary>
    /// Class for reading embedded MO files
    /// </summary>
    public class MoLocalizationService : ILocalizationService
    {
        /// <summary>
        /// The catalog containing the translations
        /// </summary>
        private readonly ICatalog catalog = new Catalog();

        /// <summary>
        /// The environment variable used to locate MO files
        /// </summary>
        public const string LOCALIZATIONDIR_ENVNAME = "LOCALIZATION_FOLDER";

        private static readonly string LOCALIZATIONDIR_VALUE =
            string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string)
                  ? Environment.GetEnvironmentVariable(LOCALIZATIONDIR_ENVNAME)
                  : AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string;

        /// <summary>
        /// Path to search for extra .mo files in
        /// </summary>
        public static readonly string[] SearchPaths =
            string.IsNullOrWhiteSpace(LOCALIZATIONDIR_VALUE)
                ? new string[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) }
                : (LOCALIZATIONDIR_VALUE).Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);


        /// <summary>
        /// Assembly to look for .mo files in
        /// </summary>
        public static readonly Assembly SearchAssembly = Assembly.GetExecutingAssembly();

        /// <summary>
        /// Regular expression to match a locale from a filename
        /// </summary>
        private static readonly Regex CI_MATCHER = new Regex(@"localization-(?<culture>" + LocalizationService.CI_MATCHER + @")\.mo");

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Localization.PoLocalizationService"/> class.
        /// </summary>
        /// <param name="ci">The culture to find.</param>
        public MoLocalizationService(CultureInfo ci)
        {
            var filenames = new string[] { 
                // Load the specialized version first
                string.Format("localization-{0}.mo", ci.Name.Replace('-', '_')), 
                // Then try the generic language version
                string.Format("localization-{0}.mo", ci.TwoLetterISOLanguageName)
            };

            foreach (var fn in filenames)
            {
                // search first in external files
                foreach (var sp in SearchPaths)
                {
                    if (!string.IsNullOrWhiteSpace(sp) && File.Exists(Path.Combine(sp, fn)))
                    {
                        using (var moFileStream = File.OpenRead(Path.Combine(sp, fn)))
                            catalog = new Catalog(moFileStream, ci);
                        return;
                    }
                }

                // search in embedded files
                if (SearchAssembly != null)
                {
                    // Find the localization streams inside the assembly
                    var names =
                        from name in SearchAssembly.GetManifestResourceNames()
                        let m = CI_MATCHER.Match(name)
                        let c = m.Success && string.Equals(m.Value, fn, StringComparison.OrdinalIgnoreCase) ? LocalizationService.ParseCulture(m.Groups["culture"].Value) : null
                        where c != null
                        select name;

                    foreach (var sn in names)
                        using (var s = SearchAssembly.GetManifestResourceStream(sn))
                            if (s != null)
                            {
                                catalog = new Catalog(s, ci);
                                return;
                            }
                }
            }
        }

        /// <summary>
        /// Pre-processes the message to use Linux line endings
        /// </summary>
        /// <param name="message">The message to pre-process</param>
        /// <returns>The pre-processed message</returns>
        private static string PreFormat(string message)
            => message?.Replace("\r\n", "\n");

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <returns>The localized string</returns>
        public string Localize(string message)
        {
            return catalog.GetString(PreFormat(message));
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <returns>The localized string</returns>
        public string Localize(string message, object arg0)
        {
            return catalog.GetString(PreFormat(message), arg0);
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
            return catalog.GetString(PreFormat(message), arg0, arg1);
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
            return catalog.GetString(PreFormat(message), arg0, arg1, arg2);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="args">The arguments</param>
        public string Localize(string message, params object[] args)
        {
            return catalog.GetString(PreFormat(message), args);
        }

        private static IEnumerable<CultureInfo> m_supportedcultures = null;

        /// <summary>
        /// Gets a list of all the supported cultures
        /// </summary>
        private static IEnumerable<CultureInfo> SupportedCultureInfos
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
                            lst = lst.Union(Directory.GetFiles(sp, "localization-*.mo"));

                    var allcultures =
                        from name in lst
                        let m = CI_MATCHER.Match(name)
                        let ci = m.Success ? LocalizationService.ParseCulture(m.Groups["culture"].Value) : null
                        where ci != null
                        select ci;

                    m_supportedcultures = allcultures.Concat(new[] { new CultureInfo("en") })
                                                     .Distinct();
                }
                return m_supportedcultures;
            }
        }

        /// <summary>
        /// Gets a list of all the supported cultures
        /// </summary>
        public static IEnumerable<string> SupportedCultures
        {
            get
            {
                return SupportedCultureInfos
                    .Select(x => x.Name).Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns true if the culture has localization support
        /// </summary>
        public static Boolean isCultureSupported(CultureInfo culture)
        {
            foreach (var supportedCulture in SupportedCultureInfos)
            {
                if (supportedCulture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
                    return true;
            }
            return false;
        }
    }
}
