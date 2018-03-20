using System.Collections.Generic;

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class MicrosoftGroup : MicrosoftGraphBackend
    {
        private const string GROUP_ID_OPTION = "group-id";

        private readonly string drivePath;

        public MicrosoftGroup() { } // Constructor needed for dynamic loading to find it

        public MicrosoftGroup(string url, Dictionary<string, string> options)
            : base(url, options)
        {
            string groupId;
            if (options.TryGetValue(GROUP_ID_OPTION, out groupId))
            {
                this.drivePath = string.Format("/groups/{0}", groupId);
            }
            else
            {
                throw new UserInformationException(Strings.MicrosoftGroup.MissingGroupId, "MicrosoftGroupMissingGroupId");
            }
        }

        public override string ProtocolKey
        {
            get { return "msgroup"; }
        }

        public override string DisplayName
        {
            get { return Strings.MicrosoftGroup.DisplayName; }
        }

        protected override string DrivePath
        {
            get { return this.drivePath; }
        }

        protected override DescriptionTemplateDelegate DescriptionTemplate
        {
            get
            {
                return Strings.MicrosoftGroup.Description;
            }
        }

        protected override IList<ICommandLineArgument> AdditionalSupportedCommands
        {
            get
            {
                return new ICommandLineArgument[]
                {
                    new CommandLineArgument(GROUP_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.MicrosoftGroup.GroupIdShort, Strings.MicrosoftGroup.GroupIdLong),
                };
            }
        }
    }
}
