using System.Collections.Generic;
using System.Linq;

using Duplicati.Library.Backend.MicrosoftGraph;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class MicrosoftGroup : MicrosoftGraphBackend
    {
        private const string GROUP_EMAIL_OPTION = "group-email";
        private const string GROUP_ID_OPTION = "group-id";
        private const string PROTOCOL_KEY = "msgroup";

        private readonly string drivePath;

        public MicrosoftGroup() { } // Constructor needed for dynamic loading to find it

        public MicrosoftGroup(string url, Dictionary<string, string> options)
            : base(url, MicrosoftGroup.PROTOCOL_KEY, options)
        {
            string groupId = null;
            string groupEmail;
            if (options.TryGetValue(GROUP_EMAIL_OPTION, out groupEmail))
            {
                groupId = this.GetGroupIdFromEmail(groupEmail);
            }

            string groupIdOption;
            if (options.TryGetValue(GROUP_ID_OPTION, out groupIdOption))
            {
                if (!string.IsNullOrEmpty(groupId) && !string.Equals(groupId, groupIdOption))
                {
                    throw new UserInformationException(Strings.MicrosoftGroup.ConflictingGroupId(groupIdOption, groupId), "MicrosoftGroupConflictingGroupId");
                }

                groupId = groupIdOption;
            }
            
            if (string.IsNullOrEmpty(groupId))
            {
                throw new UserInformationException(Strings.MicrosoftGroup.MissingGroupIdAndEmailAddress, "MicrosoftGroupMissingGroupIdAndEmailAddress");
            }

            this.drivePath = string.Format("/groups/{0}/drive", groupId);
        }

        public override string ProtocolKey
        {
            get { return MicrosoftGroup.PROTOCOL_KEY; }
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
                    new CommandLineArgument(GROUP_EMAIL_OPTION, CommandLineArgument.ArgumentType.String, Strings.MicrosoftGroup.GroupEmailShort, Strings.MicrosoftGroup.GroupEmailLong),
                };
            }
        }

        private string GetGroupIdFromEmail(string email)
        {
            // We can get all groups that have the given email as one of their addresses with:
            // https://graph.microsoft.com/v1.0/groups?$filter=mail eq '{email}' or proxyAddresses/any(x:x eq 'smtp:{email}')
            string request = string.Format("{0}/groups?$filter=mail eq '{1}' or proxyAddresses/any(x:x eq 'smtp:{1}')", this.ApiVersion, email);
            GraphCollection<Group> groups = this.Get<GraphCollection<Group>>(request);
            if (groups.Value.Length == 0)
            {
                throw new UserInformationException(Strings.MicrosoftGroup.NoGroupsWithEmail(email), "MicrosoftGroupNoGroupsWithEmail");
            }
            else if (groups.Value.Length > 1)
            {
                throw new UserInformationException(Strings.MicrosoftGroup.MultipleGroupsWithEmail(email), "MicrosoftGroupMultipleGroupsWithEmail");
            }
            else
            {
                return groups.Value.Single().Id;
            }
        }
    }
}
