using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Main
{
    public class LocaleChange : IDisposable
    {
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<LocaleChange>();

        private bool m_doResetLocale = false;
        private System.Globalization.CultureInfo m_resetLocale = null;
        private System.Globalization.CultureInfo m_resetLocaleUI = null;

        private IDisposable m_localizationContext = null;

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

        public LocaleChange(Options options)
            : this(options.HasForcedLocale ? options.ForcedLocale : null)
        {
        }
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
