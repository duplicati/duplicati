using System.Collections.Generic;

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class OneDriveV2 : MicrosoftGraphBackend
    {
        private const string DRIVE_ID_OPTION = "drive-id";

        private const string DEFAULT_DRIVE_PATH = "/me/drive";

        private readonly string drivePath;

        public OneDriveV2() { } // Constructor needed for dynamic loading to find it

        public OneDriveV2(string url, Dictionary<string, string> options)
            : base(url, options)
        {
            string driveId;
            if (options.TryGetValue(DRIVE_ID_OPTION, out driveId))
            {
                this.drivePath = string.Format("/drives/{0}", driveId);
            }
            else
            {
                this.drivePath = DEFAULT_DRIVE_PATH;
            }
        }

        public override string ProtocolKey
        {
            get { return "onedrivev2"; }
        }

        public override string DisplayName
        {
            get { return Strings.OneDriveV2.DisplayName; }
        }

        protected override string DrivePath
        {
            get { return this.drivePath; }
        }

        protected override DescriptionTemplateDelegate DescriptionTemplate
        {
            get
            {
                return Strings.OneDriveV2.Description;
            }
        }

        protected override IList<ICommandLineArgument> AdditionalSupportedCommands
        {
            get
            {
                return new ICommandLineArgument[]
                {
                    new CommandLineArgument(DRIVE_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.OneDriveV2.DriveIdShort, Strings.OneDriveV2.DriveIdLong(DEFAULT_DRIVE_PATH)),
                };
            }
        }
    }
}
