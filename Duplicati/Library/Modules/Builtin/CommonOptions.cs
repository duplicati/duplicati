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

using System;
using System.Collections.Generic;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Modules.Builtin
{
    /// <summary>
    /// Provides common options for modules.
    /// </summary>
    public class CommonOptions : Interface.IConnectionModule, IDisposable
    {
        /// <summary>
        /// Gets the key identifier for this module.
        /// </summary>
        public string Key => "common-options";

        /// <summary>
        /// Gets the display name for this module.
        /// </summary>
        public string DisplayName => Strings.CommonOptions.DisplayName;

        /// <summary>
        /// Gets the description of this module.
        /// </summary>
        public string Description => Strings.CommonOptions.Description;

        /// <summary>
        /// Gets whether this module should be loaded by default.
        /// </summary>
        public bool LoadAsDefault => true;

        /// <summary>
        /// Gets the list of supported command line arguments.
        /// </summary>
        public IList<Interface.ICommandLineArgument> SupportedCommands
            => [
                .. TimeoutOptionsHelper.GetOptions(),
                .. SslOptionsHelper.GetCertOnlyOptions(),
                .. AuthIdOptionsHelper.GetServerOnlyOptions(),
            ];

        /// <summary>
        /// Configures the module with the provided command line options.
        /// </summary>
        /// <param name="commandlineOptions">The command line options dictionary.</param>
        public void Configure(IDictionary<string, string> commandlineOptions)
        {
        }

        /// <summary>
        /// Disposes of the resources used by this instance.
        /// </summary>
        public void Dispose()
        {
        }

    }
}
