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
