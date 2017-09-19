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
        private ICatalog catalog = new Catalog();

        /// <summary>
        /// The environment variable used to locate MO files
        /// </summary>
        public const string LOCALIZATIONDIR_ENVNAME = "LOCALIZATION_FOLDER";

        /// <summary>
        /// Path to search for extra .mo files in
        /// </summary>
        public static string[] SearchPaths =
            string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string)
                ? new string[] { Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) }
                : (AppDomain.CurrentDomain.GetData(LOCALIZATIONDIR_ENVNAME) as string).Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);


        /// <summary>
        /// Assembly to look for .mo files in
        /// </summary>
        public static Assembly SearchAssembly = Assembly.GetExecutingAssembly();

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
                string.Format("localization-{0}.mo", ci.Name), 
                // Then try the generic language version
                string.Format("localization-{0}.mo", ci.TwoLetterISOLanguageName) 
            };

            foreach(var fn in filenames)
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
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <returns>The localized string</returns>
        public string Localize(string message)
        {
            return catalog.GetString(message);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="arg0">The first argument</param>
        /// <returns>The localized string</returns>
        public string Localize(string message, object arg0)
        {
            return catalog.GetString(message, arg0);
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
            return catalog.GetString(message, arg0, arg1);
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
            return catalog.GetString(message, arg0, arg1, arg2);
        }

        /// <summary>
        /// Localizes the string similar to how string.Format works
        /// </summary>
        /// <param name="message">The string to localize</param>
        /// <param name="args">The arguments</param>
        public string Localize(string message, params object[] args)
        {
            return catalog.GetString(message, args);
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
