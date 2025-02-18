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

using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class OneDrive : MicrosoftGraphBackend
    {
        private const string DRIVE_ID_OPTION = "drive-id";
        private const string DEFAULT_DRIVE_PATH = "/me/drive";
        private const string PROTOCOL_KEY = "onedrivev2";

        private readonly string drivePath;

        public OneDrive() { } // Constructor needed for dynamic loading to find it

        public OneDrive(string url, Dictionary<string, string> options)
            : base(url, OneDrive.PROTOCOL_KEY, options)
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
            get { return OneDrive.PROTOCOL_KEY; }
        }

        public override string DisplayName
        {
            get { return Strings.OneDrive.DisplayName; }
        }

        protected override string DrivePath
        {
            get { return this.drivePath; }
        }

        protected override DescriptionTemplateDelegate DescriptionTemplate
        {
            get
            {
                return Strings.OneDrive.Description;
            }
        }

        protected override IList<ICommandLineArgument> AdditionalSupportedCommands
        {
            get
            {
                return new ICommandLineArgument[]
                {
                    new CommandLineArgument(DRIVE_ID_OPTION, CommandLineArgument.ArgumentType.String, Strings.OneDrive.DriveIdShort, Strings.OneDrive.DriveIdLong(DEFAULT_DRIVE_PATH)),
                };
            }
        }
    }
}
