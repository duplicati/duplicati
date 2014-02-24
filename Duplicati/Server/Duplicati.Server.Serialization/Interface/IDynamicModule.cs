//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// The dynamic module
    /// </summary>
    public interface IDynamicModule
    {
        /// <summary>
        /// The module key
        /// </summary>
        string Key { get; }
        /// <summary>
        /// The localized module description
        /// </summary>
        string Description { get; }
        /// <summary>
        /// Gets the localized display name
        /// </summary>
        /// <value>The display name.</value>
        string DisplayName { get; }
        /// <summary>
        /// The options supported by the module
        /// </summary>
        Duplicati.Library.Interface.ICommandLineArgument[] Options { get; }
    }
}

