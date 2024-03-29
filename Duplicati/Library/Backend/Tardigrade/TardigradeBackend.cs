// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uplink.NET.Interfaces;
using uplink.NET.Models;
using uplink.NET.Services;

namespace Duplicati.Library.Backend.Tardigrade
{

    /// <summary>
    /// This backend is deprecated! It will be removed in the future.
    /// Tardigrade renamed to Storj DCS in Spring 2021 - but existing Tardigrade-Configurations could not be easily renamed.
    /// So we decided to "copy" Tardigrade over to the new name Storj DCS. In order to reduce duplicate code, the old
    /// Tardigrade-Backend hands it's logic over to Storj DCS. Only the UI-specific part, the config-parameters and
    /// the protocol-key stay here named for Tardigrade.
    /// </summary>
    public class Tardigrade : Duplicati.Library.Backend.Storj.Storj, IStreamingBackend
    {
        private const string TARDIGRADE_AUTH_METHOD = "tardigrade-auth-method";
        private const string TARDIGRADE_SATELLITE = "tardigrade-satellite";
        private const string TARDIGRADE_API_KEY = "tardigrade-api-key";
        private const string TARDIGRADE_SECRET = "tardigrade-secret";
        private const string TARDIGRADE_SHARED_ACCESS = "tardigrade-shared-access";
        private const string TARDIGRADE_BUCKET = "tardigrade-bucket";
        private const string TARDIGRADE_FOLDER = "tardigrade-folder";

        private const string PROTOCOL_KEY = "tardigrade";

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade():base()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade(string url, Dictionary<string, string> options) :base(url, options)
        {
        }

        public new string DisplayName
        {
            get { return Strings.Tardigrade.DisplayName; }
        }

        public new string ProtocolKey => PROTOCOL_KEY;

        public new IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(TARDIGRADE_AUTH_METHOD, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAuthMethodDescriptionShort, Strings.Storj.StorjAuthMethodDescriptionLong, "API key", null, null),
                    new CommandLineArgument(TARDIGRADE_SATELLITE, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSatelliteDescriptionShort, Strings.Storj.StorjSatelliteDescriptionLong, "us1.storj.io:7777", null, null),
                    new CommandLineArgument(TARDIGRADE_API_KEY, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjAPIKeyDescriptionShort, Strings.Storj.StorjAPIKeyDescriptionLong, null, null, null),
                    new CommandLineArgument(TARDIGRADE_SECRET, CommandLineArgument.ArgumentType.Password, Strings.Storj.StorjSecretDescriptionShort, Strings.Storj.StorjSecretDescriptionLong, null, null, null),
                    new CommandLineArgument(TARDIGRADE_SHARED_ACCESS, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjSharedAccessDescriptionShort, Strings.Storj.StorjSharedAccessDescriptionLong, null, null, null),
                    new CommandLineArgument(TARDIGRADE_BUCKET, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjBucketDescriptionShort, Strings.Storj.StorjBucketDescriptionLong, null, null, null),
                    new CommandLineArgument(TARDIGRADE_FOLDER, CommandLineArgument.ArgumentType.String, Strings.Storj.StorjFolderDescriptionShort, Strings.Storj.StorjFolderDescriptionLong, null, null, null),
                });
            }
        }

        public new string Description
        {
            get
            {
                return Strings.Tardigrade.Description;
            }
        }
    }
}
