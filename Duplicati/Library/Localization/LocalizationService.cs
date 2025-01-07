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
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Duplicati.Library.Localization.Short;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Localization
{
    /// <summary>
    /// Provides an entry point for localization, regardless of the underlying translation engine
    /// </summary>
    public static class LocalizationService
    {
        /// <summary>
        /// The cache of services
        /// </summary>
        private static readonly Dictionary<CultureInfo, ILocalizationService> Services = new Dictionary<CultureInfo, ILocalizationService>();

        /// <summary>
        /// The key for accessing logical context
        /// </summary>
        internal const string LOGICAL_CONTEXT_KEY = "DUPLICATI_LOCALIZATION_CULTURE_CONTEXT";

        /// <summary>
        /// Regular expression to match a locale
        /// </summary>
        public static readonly Regex CI_MATCHER = new Regex(@"[A-z]{2}([-_][A-z]{4})?([-_][A-z]{2})?");

        /// <summary>
        /// Returns a temporary disposable localization context
        /// </summary>
        /// <returns>The context that must be disposed.</returns>
        /// <param name="ci">The locale to use.</param>
        public static IDisposable TemporaryContext(CultureInfo ci)
        {
            if (ci == null)
                return null;

            return new LocalizationContext(ci);
        }

        /// <summary>
        /// A non-translating service
        /// </summary>
        private static readonly ILocalizationService InvariantService = new MockLocalizationService();

        /// <summary>
        /// Gets a localization provider with the default language
        /// </summary>
        public static ILocalizationService Invariant { get { return Get(CultureInfo.InvariantCulture); } }

        /// <summary>
        /// Parses the culture string into a cultureinfo instance.
        /// </summary>
        /// <returns>The parsed culture.</returns>
        /// <param name="culture">The culture string.</param>
        /// <param name="returninvariant">Set to <c>true</c> to return the invariant culture if the string is not valid, otherwise null is returned.</param>
        public static CultureInfo ParseCulture(string culture, bool returninvariant = false)
        {
            var ci = returninvariant ? CultureInfo.InvariantCulture : null;
            culture = culture.Replace("_", "-");

            if (CI_MATCHER.Match(culture).Success)
                try { ci = new CultureInfo(culture); }
                catch { }

            return ci;
        }

        /// <summary>
        /// Gets a localization provider with the current language
        /// </summary>
        public static ILocalizationService Current
        {
            get
            {
                var lc = CallContext.GetData(LOGICAL_CONTEXT_KEY) as string;
                if (!string.IsNullOrWhiteSpace(lc))
                    return Get(new CultureInfo(lc));
                return Get(CultureInfo.CurrentCulture);
            }
        }

        /// <summary>
        /// Gets a localization provider with the OS install language
        /// </summary>
        public static ILocalizationService Installed { get { return Get(CultureInfo.InstalledUICulture); } }

        /// <summary>
        /// Gets a localization provider with the specified language
        /// </summary>
        public static ILocalizationService Get(CultureInfo ci)
        {
            if (ci.Equals(CultureInfo.InvariantCulture))
                return InvariantService;

            ILocalizationService service;
            if (!Services.TryGetValue(ci, out service))
                service = Services[ci] = new MoLocalizationService(ci);

            return service;
        }

        /// <summary>
        /// Gets a list of all locales known by the CLR
        /// </summary>
        /// <value>All locales.</value>
        public static IEnumerable<string> AllLocales
        {
            get
            {
                return CultureInfo.GetCultures(CultureTypes.AllCultures).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x);
            }
        }

        /// <summary>
        /// Gets all cultures with localization support
        /// </summary>
        public static IEnumerable<string> SupportedCultures
        {
            get { return MoLocalizationService.SupportedCultures; }
        }

        /// <summary>
        /// Returns true if the culture has localization support
        /// </summary>
        public static Boolean isCultureSupported(CultureInfo culture)
        {
            return MoLocalizationService.isCultureSupported(culture);
        }
    }
}

