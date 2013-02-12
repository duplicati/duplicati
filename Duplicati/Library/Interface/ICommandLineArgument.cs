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
namespace Duplicati.Library.Interface
{
    /// <summary>
    /// The interface used to describe a commandline argument
    /// </summary>
    public interface ICommandLineArgument
    {
        /// <summary>
        /// A list of valid aliases, may be null or an empty array
        /// </summary>
        string[] Aliases { get; set; }

        /// <summary>
        /// A long description of the argument
        /// </summary>
        string LongDescription { get; set; }

        /// <summary>
        /// The primary name for the argument
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// A short description of the argument
        /// </summary>
        string ShortDescription { get; set; }

        /// <summary>
        /// The argument type
        /// </summary>
        CommandLineArgument.ArgumentType Type { get; set; }

        /// <summary>
        /// A list of valid values, if applicable
        /// </summary>
        string[] ValidValues { get; set; }

        /// <summary>
        /// The default value for the parameter
        /// </summary>
        string DefaultValue { get; set; }

        /// <summary>
        /// Returns a localized string indicating the argument type
        /// </summary>
        string Typename { get; }

        /// <summary>
        /// A value indicating if the option is deprecated
        /// </summary>
        bool Deprecated { get; set; }

        /// <summary>
        /// A message describing the deprecation reason and possible change suggestions
        /// </summary>
        string DeprecationMessage { get; set; }
    }
}
