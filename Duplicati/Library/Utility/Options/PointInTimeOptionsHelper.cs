// Copyright (C) 2026, The Duplicati Team
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
/// Helper for the option that pins reads of a destination to the object
/// versions that were current at a point in time
/// </summary>
public static class PointInTimeOptionsHelper
{
    /// <summary>
    /// The name of the point-in-time option
    /// </summary>
    public const string PointInTimeOption = "remote-point-in-time";

    /// <summary>
    /// The help id used when the option value cannot be parsed
    /// </summary>
    public const string HelpIdInvalidPointInTime = "InvalidPointInTime";

    /// <summary>
    /// The help id used when a write is refused because the option is set
    /// </summary>
    public const string HelpIdDestinationReadOnly = "DestinationReadOnlyAtPointInTime";

    /// <summary>
    /// Gets the command line arguments for the option
    /// </summary>
    /// <returns>The command line arguments</returns>
    public static CommandLineArgument[] GetOptions() =>
    [
        new CommandLineArgument(PointInTimeOption, CommandLineArgument.ArgumentType.DateTime, Strings.PointInTimeOptionsHelper.DescriptionShort, Strings.PointInTimeOptionsHelper.DescriptionLong)
    ];

    /// <summary>
    /// Parses the option into the UTC instant to read the destination at
    /// </summary>
    /// <remarks>
    /// A duration such as <c>7D</c> counts back from now; an absolute date and
    /// time is used as given. A value in the future reads as the current state
    /// and is clamped to now.
    /// </remarks>
    /// <param name="options">The backend options</param>
    /// <returns>The UTC instant, or <c>null</c> if the option is not set</returns>
    public static DateTime? Parse(IReadOnlyDictionary<string, string?> options)
    {
        var value = options.GetValueOrDefault(PointInTimeOption);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        DateTime parsed;
        try
        {
            parsed = Timeparser.ParseTimeInterval(value, DateTime.Now, true);
        }
        catch (Exception ex)
        {
            throw new UserInformationException(Strings.PointInTimeOptionsHelper.InvalidValueError(PointInTimeOption, value, ex.Message), HelpIdInvalidPointInTime, ex);
        }

        var utc = parsed.ToUniversalTime();
        var now = DateTime.UtcNow;
        return utc > now ? now : utc;
    }

    /// <summary>
    /// Writes the resolved point in time into the options of a wrapped backend
    /// </summary>
    /// <remarks>
    /// The instant is written as an absolute UTC time, not the raw option value, so a
    /// duration such as <c>7D</c> is not counted back again from a later clock when the
    /// wrapped backend is created or re-created.
    /// </remarks>
    /// <param name="pointInTime">The resolved UTC instant, or <c>null</c> if no point in time is set</param>
    /// <param name="target">The options of the wrapped backend</param>
    public static void ApplyTo(DateTime? pointInTime, Dictionary<string, string?> target)
    {
        if (pointInTime.HasValue)
            target[PointInTimeOption] = Utility.SerializeDateTime(pointInTime.Value);
    }

    /// <summary>
    /// Creates the exception thrown when a write is attempted while the option is set
    /// </summary>
    /// <returns>The exception to throw</returns>
    public static UserInformationException CreateReadOnlyException()
        => new(Strings.PointInTimeOptionsHelper.DestinationReadOnlyError(PointInTimeOption), HelpIdDestinationReadOnly);
}
