#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
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
