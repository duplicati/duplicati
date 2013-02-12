#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.DynamicLoader
{
    /// <summary>
    /// An interface class that provides easy loading of plugable setting controls
    /// </summary>
    public static class SettingsControlLoader
    {
        /// <summary>
        /// Implementation overrides specific to encryption
        /// </summary>
        private class SettingsControlLoaderSub : DynamicLoader<ISettingsControl>
        {
            /// <summary>
            /// Returns the filename extension, which is also the key
            /// </summary>
            /// <param name="item">The item to load the key for</param>
            /// <returns>The file extension used by the module</returns>
            protected override string GetInterfaceKey(ISettingsControl item)
            {
                return item.Key;
            }

            /// <summary>
            /// Returns the subfolders searched for encryption modules
            /// </summary>
            protected override string[] Subfolders
            {
                get { return new string[] { "settingcontrols" }; }
            }
        }

        #region Public static API

        /// <summary>
        /// Gets a list of loaded settings controls, the instances can be used to extract interface information, not used to interact with the module.
        /// </summary>
        public static ISettingsControl[] Modules { get { return new SettingsControlLoaderSub().Interfaces; } }

        #endregion

    }
}
