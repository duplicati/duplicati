using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.License
{
    /// <summary>
    /// Class that represents a single license entry
    /// </summary>
    public class LicenseEntry
    {
        /// <summary>
        /// The component title
        /// </summary>
        public string Title;
        /// <summary>
        /// The homepage of the component
        /// </summary>
        public string Url;
        /// <summary>
        /// The license for the component
        /// </summary>
        public string License;

        /// <summary>
        /// Constructs a new license entry
        /// </summary>
        /// <param name="title">The component title</param>
        /// <param name="urlfile">The homepage of the component</param>
        /// <param name="licensefile">The license for the component</param>
        public LicenseEntry(string title, string urlfile, string licensefile)
        {
            Title = title;
            if (!string.IsNullOrEmpty(urlfile) && System.IO.File.Exists(urlfile))
                Url = System.IO.File.ReadAllText(urlfile).Trim();
            License = System.IO.File.ReadAllText(licensefile);
            if (License.IndexOf("\r\n") < 0)
                License = License.Replace("\n", "\r\n").Replace("\r", "\r\n");
            if (Environment.NewLine != "\r\n")
                License = License.Replace("\r\n", Environment.NewLine);

        }

        /// <summary>
        /// Returns the title of the item as the identification
        /// </summary>
        /// <returns>The item title</returns>
        public override string ToString()
        {
            return Title;
        }
    }
}
