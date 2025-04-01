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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Changes the current locale for all threads and for log messages in the current context.
    /// When disposed the changes are reset.
    /// </summary>
    public class LocaleChange : IDisposable
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocaleChange>();

        private bool m_doResetLocale = false;
        private System.Globalization.CultureInfo m_resetLocale = null;
        private System.Globalization.CultureInfo m_resetLocaleUI = null;

        private IDisposable m_localizationContext = null;

        /// <summary>
        /// Change locale if newLocale is not null
        /// </summary>
        public LocaleChange(System.Globalization.CultureInfo newLocale)
        {
            if (newLocale == null)
            {
                return;
            }
            try
            {
                DoGetLocale(out m_resetLocale, out m_resetLocaleUI);
                m_doResetLocale = true;
                // Wrap the call to avoid loading issues for the setLocale method
                DoSetLocale(newLocale, newLocale);
                m_localizationContext = Localization.LocalizationService.TemporaryContext(newLocale);
            }
            catch (MissingMethodException ex)
            {
                m_doResetLocale = false;
                m_resetLocale = m_resetLocaleUI = null;

                Library.Logging.Log.WriteWarningMessage(LOGTAG, "LocaleChangeError", ex, Strings.Controller.FailedForceLocaleError(ex.Message));
            }
        }

        /// <summary>
        /// Change locale if ForcedLocale is specified in options
        /// </summary>
        /// <param name="options">Command line options</param>
        /// <exception cref="System.Globalization.CultureNotFoundException">
        /// Thrown if specified locale was not found.
        /// </exception>
        public LocaleChange(Options options)
            : this(options.HasForcedLocale ? options.ForcedLocale : null)
        {
        }
        /// <summary>
        /// Change locale if 'force-locale' is specified in options dictionary
        /// </summary>
        /// <param name="options">Command line options</param>
        /// <exception cref="System.Globalization.CultureNotFoundException">
        /// Thrown if specified locale was not found.
        /// </exception>
        public LocaleChange(Dictionary<string, string> options)
            : this(new Options(options))
        {
        }

        public void Dispose()
        {
            if (m_doResetLocale)
            {
                // Wrap the call to avoid loading issues for the setLocale method
                DoSetLocale(m_resetLocale, m_resetLocaleUI);

                m_doResetLocale = false;
                m_resetLocale = null;
                m_resetLocaleUI = null;
            }
            if(m_localizationContext != null)
            {
                m_localizationContext.Dispose();
                m_localizationContext = null;
            }
        }

        /// <summary>
        /// Attempts to get the locale, but delays linking to the calls as they are missing in some environments
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoGetLocale(out System.Globalization.CultureInfo locale, out System.Globalization.CultureInfo uiLocale)
        {
            locale = System.Globalization.CultureInfo.DefaultThreadCurrentCulture;
            uiLocale = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture;
        }

        /// <summary>
        /// Attempts to set the locale, but delays linking to the calls as they are missing in some environments
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void DoSetLocale(System.Globalization.CultureInfo locale, System.Globalization.CultureInfo uiLocale)
        {
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = locale;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = uiLocale;
        }

    }
}
