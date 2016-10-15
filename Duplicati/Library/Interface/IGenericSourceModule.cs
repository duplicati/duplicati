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
    /// An interface for a plugable source module.
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
        /// <returns>A list of changed or added options values</returns>
        Dictionary<string, string> ParseSource(ref string[] paths, ref string filter);

        /// <summary>
        /// This method is the interception where the module can interact with the execution environment and modify the settings.
        /// </summary>
        /// <param name="commandlineOptions">A set of commandline options passed to Duplicati</param>
        /// <returns>If module is going to backup anything, it returns true, otherwise false</returns>
        bool ContainFiles(Dictionary<string, string> commandlineOptions);
    }
}
