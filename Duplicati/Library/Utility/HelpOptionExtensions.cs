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
using System.CommandLine.Builder;
using System.Linq;

namespace Duplicati.Library.Utility;

public static class HelpOptionExtensions
{
    /// <summary>
    /// Aliases to support for help command
    /// </summary>
    private static readonly string[] AlternativeHelpStrings = ["help", "/help","--help", "usage", "/usage", "--usage", "/h", "-h"];

    /// <summary>
    /// Adds the following aliases: "help", "/help", "--usage", "/usage".
    /// These aliases will work the same way as the default help options (-h, --help).
    /// </summary>
    /// <param name="builder">The command line builder to extend.</param>
    /// <returns>The command line builder with additional help aliases.</returns>
    public static CommandLineBuilder UseAdditionalHelpAliases(this CommandLineBuilder builder)
    {
        var rootHelpOption = builder.Command.Options.FirstOrDefault(o => o.GetType().Name == "HelpOption");
        if (rootHelpOption == null) return builder;
        foreach (var alias in AlternativeHelpStrings)
            if (!rootHelpOption.Aliases.Contains(alias))
                rootHelpOption.AddAlias(alias);

        return builder;
    }
    
    /// <summary>
    /// Returns true if any of the arguments are any of the help strings.
    /// </summary>
    /// <param name="args">Arguments</param>
    public static bool IsArgumentAnyHelpString(IEnumerable<string> args) 
        => args != null && AlternativeHelpStrings.Any(x => args.Contains(x, StringComparer.OrdinalIgnoreCase));
}