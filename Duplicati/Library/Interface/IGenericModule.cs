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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for a pluggable generic module.
    /// An instance of a module is loaded prior to a backup or restore operation,
    /// and can perform tasks relating to the general execution environment, as
    /// well as modify the options used in Duplicati.
    /// 
    /// The implementation must have a default constructor.
    /// If the module is actually loaded, the Configure method is called.
    /// All instances where the Configure method is called will be disposed,
    /// if they implement the IDisposable interface as well.
    /// </summary>
    public interface IGenericModule : IDynamicModule
    {
        /// <summary>
        /// The module key, used to activate or deactivate the module on the commandline
        /// </summary>
        string Key { get; }

        /// <summary>
        /// A localized string describing the module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the module
        /// </summary>
        string Description { get; }

        /// <summary>
        /// A boolean value that indicates if the module should always be loaded.
        /// If true, the  user can choose to not load the module by entering the appropriate commandline option.
        /// If false, the user can choose to load the module by entering the appropriate commandline option.
        /// </summary>
        bool LoadAsDefault { get; }

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        void Configure(IDictionary<string, string> commandlineOptions);
    }
}
