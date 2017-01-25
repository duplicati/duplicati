//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// A module that displays a set of options in the general setup area
    /// </summary>
    public interface IGenericServerModule : IGenericModule
    {
        /// <summary>
        /// Gets a list of supported global module arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedGlobalCommands { get; }

        /// <summary>
        /// Gets a list of supported local module arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedLocalCommands { get; }

        /// <summary>
        /// Gets an optional link that helps the user understand this server module
        /// </summary>
        string ServiceLink { get; }
    }
}
