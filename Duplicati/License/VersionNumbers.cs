using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.License
{
    public static class VersionNumbers
    {
        public static string Version
        {
            get
            {
#if DEBUG
                string debug = " - DEBUG";
#else
                string debug = "";
#endif

                return VersionNumber + debug;
            }
        }

        private static string VersionNumber
        {
            get
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;


                if (v == new Version(1, 0, 0, 183))
                    return "1.0";
                else if (v == new Version(1, 99, 0, 605))
                    return "1.2 beta 1";
                else
                    return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }
    }
}
