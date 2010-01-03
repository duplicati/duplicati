#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.Library.Backend
{
    /// <summary>
    /// Primary implementation of the <see cref="ICommandLineArgument">ICommandLineArgument</see> interface.
    /// </summary>
    public class CommandLineArgument : Duplicati.Library.Backend.ICommandLineArgument
    {
        public enum ArgumentType
        {
            /// <summary>
            /// Indicates that the argument is a string type
            /// </summary>
            String,
            /// <summary>
            /// Indicates that the argument is an integer type
            /// </summary>
            Integer,
            /// <summary>
            /// Indicates that the argument is a boolean type
            /// </summary>
            Boolean,
            /// <summary>
            /// Indicates that the argument is a timespan type
            /// </summary>
            Timespan,
            /// <summary>
            /// Indicates that the argument is a size type
            /// </summary>
            Size,
            /// <summary>
            /// Indicates that the argument is an enumeration value
            /// </summary>
            Enumeration,
            /// <summary>
            /// Indicates that the argument is a path to a file or directory
            /// </summary>
            Path,
            /// <summary>
            /// The argument type is unknown
            /// </summary>
            Unknown
        }

        private string m_name;
        private string[] m_aliases = null;
        private ArgumentType m_type = ArgumentType.Unknown;
        private string[] m_validValues = null;
        private string m_shortDescription = "";
        private string m_longDescription = "";
        private string m_defaultValue = "";

        /// <summary>
        /// The primary name for the argument
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// A list of valid aliases, may be null or an empty array
        /// </summary>
        public string[] Aliases
        {
            get { return m_aliases; }
            set { m_aliases = value; }
        }

        /// <summary>
        /// The argument type
        /// </summary>
        public ArgumentType Type
        {
            get { return m_type; }
            set { m_type = value; }
        }

        /// <summary>
        /// A list of valid values, if applicable
        /// </summary>
        public string[] ValidValues
        {
            get { return m_validValues; }
            set { m_validValues = value; }
        }

        /// <summary>
        /// A short description of the argument
        /// </summary>
        public string ShortDescription
        {
            get { return m_shortDescription; }
            set { m_shortDescription = value; }
        }

        /// <summary>
        /// A long description of the argument
        /// </summary>
        public string LongDescription
        {
            get { return m_longDescription; }
            set { m_longDescription = value; }
        }

        /// <summary>
        /// The default value for the parameter
        /// </summary>
        public string DefaultValue
        {
            get { return m_defaultValue; }
            set { m_defaultValue = value; }
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        public CommandLineArgument(string name)
        {
            m_name = name;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        public CommandLineArgument(string name, ArgumentType type)
            : this(name)
        {
            m_type = type;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription)
            : this(name, type)
        {
            m_shortDescription = shortDescription;
            m_longDescription = longDescription;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        /// <param name="defaultValue">The default value of the argumen</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue)
            : this(name, type, shortDescription, longDescription)
        {
            m_defaultValue = defaultValue;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        /// <param name="defaultValue">The default value of the argumen</param>
        /// <param name="aliases">A list of aliases for the command</param>
        /// <param name="values">A list of valid values for the command</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue, string[] aliases)
            : this(name, type, shortDescription, longDescription, defaultValue)
        {
            m_aliases = aliases;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        /// <param name="defaultValue">The default value of the argumen</param>
        /// <param name="aliases">A list of aliases for the command</param>
        /// <param name="values">A list of valid values for the command</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue, string[] aliases, string[] values)
            : this(name, type, shortDescription, longDescription, defaultValue)
        {
            m_aliases = aliases;
            m_validValues = values;
        }

        /// <summary>
        /// Returns a localized string indicating the argument type
        /// </summary>
        public string Typename
        {
            get
            {
                switch (this.Type)
                {
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Boolean:
                        return Strings.DataTypes.Boolean;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Enumeration:
                        return Strings.DataTypes.Enumeration;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Integer:
                        return Strings.DataTypes.Integer;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Path:
                        return Strings.DataTypes.Path;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Size:
                        return Strings.DataTypes.Size;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.String:
                        return Strings.DataTypes.String;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Timespan:
                        return Strings.DataTypes.Timespan;
                    case Duplicati.Library.Backend.CommandLineArgument.ArgumentType.Unknown:
                        return Strings.DataTypes.Unknown;
                    default:
                        return this.Type.ToString();
                }

            }
        }

        public static void PrintArgument(List<string> lines, Duplicati.Library.Backend.ICommandLineArgument arg)
        {
            lines.Add(" --" + arg.Name + " (" + arg.Typename + "): " + arg.ShortDescription);
            lines.Add("   " + arg.LongDescription);
            if (arg.Aliases != null && arg.Aliases.Length > 0)
                lines.Add("   * " + Strings.CommandLineArgument.AliasesHeader + ": --" + string.Join(", --", arg.Aliases));

            if (arg.ValidValues != null && arg.ValidValues.Length > 0)
                lines.Add("   * " + Strings.CommandLineArgument.ValuesHeader + ": " + string.Join(", ", arg.ValidValues));

            if (!string.IsNullOrEmpty(arg.DefaultValue))
                lines.Add("   * " + Strings.CommandLineArgument.DefaultValueHeader + ": " + arg.DefaultValue);

        }

    }
}
