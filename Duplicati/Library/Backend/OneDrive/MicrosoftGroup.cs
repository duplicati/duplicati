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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.MicrosoftGraph;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

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
                // TODO: Should not make network requests in constructor
                groupId = this.GetGroupIdFromEmailAsync(groupEmail, CancellationToken.None).Await();
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
                return
                [
                    new CommandLineArgument(GROUP_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.MicrosoftGroup.GroupIdShort, Strings.MicrosoftGroup.GroupIdLong),
                    new CommandLineArgument(GROUP_EMAIL_OPTION, CommandLineArgument.ArgumentType.String, Strings.MicrosoftGroup.GroupEmailShort, Strings.MicrosoftGroup.GroupEmailLong),
                ];
            }
        }

        private async Task<string> GetGroupIdFromEmailAsync(string email, CancellationToken cancelToken)
        {
            // We can get all groups that have the given email as one of their addresses with:
            // https://graph.microsoft.com/v1.0/groups?$filter=mail eq '{email}' or proxyAddresses/any(x:x eq 'smtp:{email}')
            string request = string.Format("{0}/groups?$filter=mail eq '{1}' or proxyAddresses/any(x:x eq 'smtp:{1}')", this.ApiVersion, email);
            var groups = await this.GetAsync<GraphCollection<Group>>(request, cancelToken).ConfigureAwait(false);
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
