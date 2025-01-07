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
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Primary implementation of the <see cref="ICommandLineArgument">ICommandLineArgument</see> interface.
    /// </summary>
    [Serializable]
    public class CommandLineArgument : Duplicati.Library.Interface.ICommandLineArgument
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
            /// Indicates that the argument is a password and should be masked
            /// </summary>
            Password,
            /// <summary>
            /// Indicates that the argument is an enumeration value supporting a combination of flags
            /// </summary>
            Flags,
            /// <summary>
            /// Indicates that the argument is a decimal value
            /// </summary>
            Decimal,
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
        private bool m_deprecated = false;
        private string m_deprecationMessage = "";

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
        /// A value indicating if the option is deprecated
        /// </summary>
        public bool Deprecated
        {
            get { return m_deprecated; }
            set { m_deprecated = value; }
        }

        /// <summary>
        /// A message describing the deprecation reason and possible change suggestions
        /// </summary>
        public string DeprecationMessage
        {
            get { return m_deprecationMessage; }
            set { m_deprecationMessage = value; }
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
            if (type == ArgumentType.Boolean)
                m_defaultValue = "false";
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
        /// <param name="defaultValue">The default value of the argument</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue)
            : this(name, type, shortDescription, longDescription)
        {
            m_defaultValue = defaultValue;
            if (type == ArgumentType.Boolean && string.IsNullOrEmpty(m_defaultValue))
                m_defaultValue = "false";
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        /// <param name="defaultValue">The default value of the argument</param>
        /// <param name="aliases">A list of aliases for the command</param>
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
        /// <param name="defaultValue">The default value of the argument</param>
        /// <param name="aliases">A list of aliases for the command</param>
        /// <param name="values">A list of valid values for the command</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue, string[] aliases, string[] values)
            : this(name, type, shortDescription, longDescription, defaultValue)
        {
            m_aliases = aliases;
            m_validValues = values;
        }

        /// <summary>
        /// Creates a new CommandLineArgument instance
        /// </summary>
        /// <param name="name">The name of the argument</param>
        /// <param name="type">The argument type</param>
        /// <param name="shortDescription">The arguments short description</param>
        /// <param name="longDescription">The arguments long description</param>
        /// <param name="defaultValue">The default value of the argument</param>
        /// <param name="aliases">A list of aliases for the command</param>
        /// <param name="values">A list of valid values for the command</param>
        /// <param name="deprecationMessage">A message indicating the reason for deprecation and any change suggestions</param>
        public CommandLineArgument(string name, ArgumentType type, string shortDescription, string longDescription, string defaultValue, string[] aliases, string[] values, string deprecationMessage)
            : this(name, type, shortDescription, longDescription, defaultValue, aliases, values)
        {
            m_deprecated = true;
            m_deprecationMessage = deprecationMessage;
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
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean:
                        return Strings.DataTypes.Boolean;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Enumeration:
                        return Strings.DataTypes.Enumeration;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Flags:
                        return Strings.DataTypes.Flags;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Integer:
                        return Strings.DataTypes.Integer;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Path:
                        return Strings.DataTypes.Path;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Size:
                        return Strings.DataTypes.Size;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.String:
                        return Strings.DataTypes.String;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Timespan:
                        return Strings.DataTypes.Timespan;
                    case Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Unknown:
                        return Strings.DataTypes.Unknown;
                    default:
                        return this.Type.ToString();
                }

            }
        }

        public static void PrintArgument(List<string> lines, ICommandLineArgument arg)
        {
            PrintArgument(lines, arg, " ");
        }

        public static void PrintArgument(List<string> lines, ICommandLineArgument arg, string indent)
        {
            lines.Add(indent + "--" + arg.Name + " (" + arg.Typename + "): " + arg.ShortDescription);

            if (arg.Deprecated)
                lines.Add(indent + "  " + Strings.CommandLineArgument.DeprecationMarker + ": " + arg.DeprecationMessage);

            lines.Add(indent + "  " + arg.LongDescription);
            if (arg.Aliases != null && arg.Aliases.Length > 0)
                lines.Add(indent + "  * " + Strings.CommandLineArgument.AliasesHeader + ": --" + string.Join(", --", arg.Aliases));

            if (arg.ValidValues != null && arg.ValidValues.Length > 0)
                lines.Add(indent + "  * " + Strings.CommandLineArgument.ValuesHeader + ": " + string.Join(", ", arg.ValidValues));

            if (!string.IsNullOrEmpty(arg.DefaultValue))
                lines.Add(indent + "  * " + Strings.CommandLineArgument.DefaultValueHeader + ": " + arg.DefaultValue);


        }

    }
}
