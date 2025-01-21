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

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// An interface for a pluggable source module.
    /// An instance of a module is loaded prior to a backup or restore operation,
    /// and can perform tasks relating to the source plugins, as
    /// well as modify backup source, the options and filters used in Duplicati.
    /// </summary>
    public interface IGenericSourceModule : IGenericModule
    {
        /// <summary>
        /// This method parse and alter backup source paths, apply and alter filters and returns changed or added options values.
        /// </summary>
        /// <param name="paths">Backup source paths</param>
        /// <param name="filter">Filters that are applied to backup paths (include, exclude)</param>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        /// <returns>A list of changed or added options values</returns>
        Dictionary<string, string> ParseSourcePaths(ref string[] paths, ref string filter, Dictionary<string, string> commandlineOptions);

        /// <summary>
        /// This method decides if input variables contains something to backup.
        /// </summary>
        /// <param name="paths">A set of source paths</param>
        /// <returns>If module is going to backup anything, it returns true, otherwise false</returns>
        bool ContainFilesForBackup(string[] paths);
    }
}
