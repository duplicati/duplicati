using System.Collections.Generic;

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class SharePointV2 : MicrosoftGraphBackend
    {
        private const string SITE_ID_OPTION = "site-id";

        private readonly string drivePath;

        public SharePointV2() { } // Constructor needed for dynamic loading to find it

        public SharePointV2(string url, Dictionary<string, string> options)
            : base(url, options)
        {
            string siteId;
            if (options.TryGetValue(SITE_ID_OPTION, out siteId))
            {
                this.drivePath = string.Format("/sites/{0}", siteId);
            }
            else
            {
                throw new UserInformationException(Strings.SharePointV2.MissingSiteId);
            }
        }

        public override string ProtocolKey
        {
            get { return "sharepoint"; }
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
    }
}
