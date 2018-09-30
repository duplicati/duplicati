using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Duplicati.Library.Backend.MicrosoftGraph;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class SharePointV2 : MicrosoftGraphBackend
    {
        private const string SITE_ID_OPTION = "site-id";
        private const string PROTOCOL_KEY = "sharepoint";

        private readonly string drivePath;
        private string siteId = null;

        public SharePointV2() { } // Constructor needed for dynamic loading to find it

        public SharePointV2(string url, Dictionary<string, string> options)
            : base(url, options)
        {
            // Check to see if a site ID was explicitly provided
            string siteIdOption;
            if (options.TryGetValue(SITE_ID_OPTION, out siteIdOption))
            {
                if (!string.IsNullOrEmpty(this.siteId) && !string.Equals(this.siteId, siteIdOption))
                {
                    throw new UserInformationException(Strings.SharePointV2.ConflictingSiteId(siteIdOption, this.siteId), "SharePointConflictingSiteId");
                }

                this.siteId = siteIdOption;
            }

            if (string.IsNullOrEmpty(this.siteId))
            {
                throw new UserInformationException(Strings.SharePointV2.MissingSiteId, "SharePointMissingSiteId");
            }

            this.drivePath = string.Format("/sites/{0}/drive", this.siteId);
        }

        public override string ProtocolKey
        {
            get { return SharePointV2.PROTOCOL_KEY; }
        }

        public override string DisplayName
        {
            get { return Strings.SharePointV2.DisplayName; }
        }

        protected override string DrivePath
        {
            get { return this.drivePath; }
        }

        protected override DescriptionTemplateDelegate DescriptionTemplate
        {
            get
            {
                return Strings.SharePointV2.Description;
            }
        }

        protected override IList<ICommandLineArgument> AdditionalSupportedCommands
        {
            get
            {
                return new ICommandLineArgument[]
                {
                    new CommandLineArgument(SITE_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.SharePointV2.SiteIdShort, Strings.SharePointV2.SiteIdLong),
                };
            }
        }

        /// <summary>
        /// This method takes an input URL, which could be either in the format that the old SharePoint backend accepted or
        /// just the host and path (effectively, just the path) that the graph base backend expects,
        /// and converts it to just the local path within the drive.
        /// 
        /// At the same time, if the given URL is in the form of a full SharePoint site URL, then it also determines the site-id and stores it.
        /// </summary>
        /// <param name="url">Input URL</param>
        /// <returns>Path within the drive</returns>
        protected override string GetRootPathFromUrl(string url)
        {
            Uri uri = new Uri(url);

            // If the user gave a URL like "https://{tenant}.sharepoint.com/path" in the UI,
            // it might appear here as "sharepoint://https://{tenant}.sharepoint.com/path".
            // If that's the case, the URIs Host will be https, and we should create a new URI without it.
            if (string.Equals(uri.Host, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "http", StringComparison.OrdinalIgnoreCase))
            {
                // LocalPath will already be prefixed by "//", due to the https:// part.
                uri = new Uri(string.Format("{0}:{1}", uri.Scheme, uri.LocalPath));
            }

            SharePointSite site = this.GetSharePointSite(uri);
            if (site != null)
            {
                // Get the web URL of the site's main drive
                try
                {
                    Drive drive = this.Get<Drive>(string.Format("{0}/sites/{1}/drive", this.ApiVersion, site.Id));

                    this.siteId = site.Id;
                    Uri driveWebUrl = new Uri(drive.WebUrl);

                    // Make sure to replace any "//" in the original path with "/", so the substrings line up.
                    return uri.LocalPath.Replace("//", "/").Substring(driveWebUrl.LocalPath.Length);
                }
                catch (MicrosoftGraphException)
                {
                    // Couldn't get the drive info, so assume the URL we were given isn't actually a full SharePoint site.
                }
            }

            return base.GetRootPathFromUrl(url);
        }

        private SharePointSite GetSharePointSite(Uri url)
        {
            UriBuilder uri = new UriBuilder(url);

            // We can get a SharePoint site's info by querying /v1.0/sites/{hostname}:{siteWebPath}.
            // Since this full URL likely has the web path as some subpart of it, we check against each subpath to see if that is a site,
            // and if it is, we record the site ID.
            string requestBase = string.Format("{0}/sites/{1}", this.ApiVersion, uri.Host);

            // Just like the original SharePoint backend, use the "//" as a hint at where the site might be
            int siteHint = uri.Path.IndexOf("//", StringComparison.Ordinal);
            if (siteHint >= 0)
            {
                try
                {
                    string request = string.Format("{0}:/{1}", requestBase, uri.Path.Substring(0, siteHint));
                    return this.Get<SharePointSite>(request);
                }
                catch (MicrosoftGraphException)
                {
                    // This isn't the right path
                }
            }

            string[] pathPieces = uri.Path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = pathPieces.Length; i > 0; i--)
            {
                try
                {
                    string request = string.Format("{0}:/{1}", requestBase, string.Join("/", pathPieces.Take(i)));
                    return this.Get<SharePointSite>(request);
                }
                catch (MicrosoftGraphException)
                {
                    // This isn't the right path
                }
            }

            // Couldn't find the site
            return null;
        }
    }
}
