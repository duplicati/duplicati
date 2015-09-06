//  Copyright (C) 2015, The Duplicati Team

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
    public interface IWebModule
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
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }
        
        /// <summary>
        /// Execute the specified command with the given options.
        /// </summary>
        /// <param name="options">The options to use</param>
        /// <returns>A list of output values</returns>
        IDictionary<string, string> Execute(IDictionary<string, string> options);
        
    }
}

