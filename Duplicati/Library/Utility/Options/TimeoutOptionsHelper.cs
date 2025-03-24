
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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Utility.Options;

/// <summary>
/// Helper class for unified use of timeout settings
/// </summary>
public static class TimeoutOptionsHelper
{
    /// <summary> 
    /// The default timeout in seconds for List operations
    /// </summary> 
    public const string DefaultListOperationTimeout = "10m";

    /// <summary>
    /// The default timeout in seconds for Delete/CreateFolder operations
    /// </summary>
    public const string DefaultShortOperationTimeout = "30s";

    /// <summary>
    /// The timeout for no activity during transfers
    /// </summary>
    public const string DefaultReadWriteTimeout = "30s";

    /// <summary>
    /// The name of the short timeout option, without a prefix
    /// </summary>
    public const string ShortTimeoutOption = "short-timeout";
    /// <summary>
    /// The name of the list timeout option, without a prefix
    /// </summary>
    public const string ListTimeoutOption = "list-timeout";
    /// <summary>
    /// The name of the read-write timeout option, without a prefix
    /// </summary>
    public const string ReadWriteTimeoutOption = "read-write-timeout";

    /// <summary>
    /// Gets the timeout options
    /// </summary>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The timeout options</returns>
    public static CommandLineArgument[] GetOptions(string? prefix = null) =>
    [
        new CommandLineArgument($"{prefix}{ShortTimeoutOption}", CommandLineArgument.ArgumentType.Timespan, Strings.TimeoutSettingsHelper.DescriptionShortTimeoutShort, Strings.TimeoutSettingsHelper.DescriptionShortTimeoutLong, DefaultShortOperationTimeout),
        new CommandLineArgument($"{prefix}{ListTimeoutOption}", CommandLineArgument.ArgumentType.Timespan, Strings.TimeoutSettingsHelper.DescriptionListTimeoutShort, Strings.TimeoutSettingsHelper.DescriptionListTimeoutLong, DefaultListOperationTimeout),
        new CommandLineArgument($"{prefix}{ReadWriteTimeoutOption}", CommandLineArgument.ArgumentType.Timespan, Strings.TimeoutSettingsHelper.DescriptionReadWriteTimeoutShort, Strings.TimeoutSettingsHelper.DescriptionReadWriteTimeoutLong, DefaultReadWriteTimeout)
    ];

    /// <summary>
    /// Parses the timeout options from an options dictionary
    /// </summary>
    /// <param name="options">The options dictionary</param>
    /// <param name="prefix">An optional prefix for the options</param>
    /// <returns>The timeout configuration</returns>
    public static Timeouts Parse(IReadOnlyDictionary<string, string?> options, string? prefix = null)
        => new Timeouts(
            Utility.ParseTimespanOption(options, $"{prefix}{ShortTimeoutOption}", DefaultShortOperationTimeout),
            Utility.ParseTimespanOption(options, $"{prefix}{ListTimeoutOption}", DefaultListOperationTimeout),
            Utility.ParseTimespanOption(options, $"{prefix}{ReadWriteTimeoutOption}", DefaultReadWriteTimeout)
        );

    /// <summary>
    /// Structure for timeout configuration
    /// </summary>
    /// <param name="ShortTimeout">The timeout in seconds for short operations like delete and create folder</param>
    /// <param name="ListTimeout">The timeout in seconds for listing files and folders</param>
    /// <param name="ReadWriteTimeout">The timeout in seconds for read and write operations.</param>
    public sealed record Timeouts(TimeSpan ShortTimeout, TimeSpan ListTimeout, TimeSpan ReadWriteTimeout);
}