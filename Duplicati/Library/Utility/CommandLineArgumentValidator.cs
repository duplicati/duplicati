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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility;

/// <summary>
/// Helper class for validating command line arguments
/// </summary>
public static class CommandLineArgumentValidator
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(CommandLineArgumentValidator));

    /// <summary>
    /// Validates arguments against the supported commands
    /// </summary>
    /// <param name="arguments">The arguments supported<</param>
    public static void ValidateArguments(IEnumerable<ICommandLineArgument> arguments, IReadOnlyDictionary<string, string?> options, IReadOnlySet<string> knownDuplicateOptions, IReadOnlySet<string> IgnoredOptions)
    {
        var allOptionsGrouped = arguments
            .SelectMany(option => (option.Aliases ?? []).Prepend(option.Name).Select(name => (Name: name, Option: option)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase);

#if DEBUG
        foreach (var n in allOptionsGrouped.Where(x => !knownDuplicateOptions.Contains(x.Key) && x.Count() > 1))
            Logging.Log.WriteErrorMessage(LOGTAG, "DuplicateOption", null, $"Duplicate option: {n.Key}");
#endif

        var allOptions = allOptionsGrouped
            .ToDictionary(x => x.Key, x => x.First().Option, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in options.Where(x => !IgnoredOptions.Contains(x.Key)))
        {
            if (!allOptions.TryGetValue(key, out var option) || option == null)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "UnknownOption", null, $"Unknown option supplied: {key}");
                continue;
            }

            var validationMessage = ValidateOptionValue(option, key, value);
            if (!string.IsNullOrWhiteSpace(validationMessage))
                Logging.Log.WriteWarningMessage(LOGTAG, "OptionValidationError", null, validationMessage);
        }
    }

    /// <summary>
    /// Checks if the value passed to an option is actually valid.
    /// </summary>
    /// <param name="arg">The argument being validated</param>
    /// <param name="optionname">The name of the option to validate</param>
    /// <param name="value">The value to check</param>
    /// <returns>Null if no errors are found, an error message otherwise</returns>
    public static string? ValidateOptionValue(ICommandLineArgument arg, string optionname, string? value)
    {
        if (arg.Type == CommandLineArgument.ArgumentType.Enumeration)
        {
            var found = false;
            foreach (var v in arg.ValidValues ?? [])
                if (string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

            if (!found)
                return Strings.CommandLineArgumentValidator.UnsupportedEnumerationValue(optionname, value, arg.ValidValues ?? new string[0]);

        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Flags)
        {
            var validatedAllFlags = false;
            var flags = (value ?? string.Empty).ToLowerInvariant().Split(new[] { "," }, StringSplitOptions.None).Select(flag => flag.Trim()).Distinct();
            var validFlags = arg.ValidValues ?? [];

            foreach (var flag in flags)
            {
                if (!validFlags.Any(validFlag => string.Equals(validFlag, flag, StringComparison.OrdinalIgnoreCase)))
                {
                    validatedAllFlags = false;
                    break;
                }

                validatedAllFlags = true;
            }

            if (!validatedAllFlags)
                return Strings.CommandLineArgumentValidator.UnsupportedFlagsValue(optionname, value, validFlags);
        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Boolean)
        {
            if (!string.IsNullOrEmpty(value) && Utility.ParseBool(value, true) != Utility.ParseBool(value, false))
                return Strings.CommandLineArgumentValidator.UnsupportedBooleanValue(optionname, value);
        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Integer)
        {
            if (!long.TryParse(value, out _))
                return Strings.CommandLineArgumentValidator.UnsupportedIntegerValue(optionname, value);
        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Path)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Strings.CommandLineArgumentValidator.UnsupportedPathValue(optionname, value);

            foreach (var p in value.Split(Path.DirectorySeparatorChar))
                if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    return Strings.CommandLineArgumentValidator.UnsupportedPathValue(optionname, p);
        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Size)
        {
            try
            {
                Sizeparser.ParseSize(value);
            }
            catch
            {
                return Strings.CommandLineArgumentValidator.UnsupportedSizeValue(optionname, value);
            }

            if (!string.IsNullOrWhiteSpace(value) && char.IsDigit(value.Last()))
                return Strings.CommandLineArgumentValidator.NonQualifiedSizeValue(optionname, value);
        }
        else if (arg.Type == CommandLineArgument.ArgumentType.Timespan)
        {
            try
            {
                Timeparser.ParseTimeSpan(value);
            }
            catch
            {
                return Strings.CommandLineArgumentValidator.UnsupportedTimeValue(optionname, value);
            }
        }

        return null;
    }

}