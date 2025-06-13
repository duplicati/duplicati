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

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The backup and schedule DTO
/// </summary>
public sealed record BackupAndScheduleInputDto
{
    /// <summary>
    /// The backup DTO
    /// </summary>
    public required BackupInputDto Backup { get; init; }
    /// <summary>
    /// The schedule DTO
    /// </summary>
    public ScheduleInputDto? Schedule { get; init; }

    /// <summary>
    /// The backup DTO
    /// </summary>
    public sealed record BackupInputDto
    {
        /// <summary>
        /// The backup name
        /// </summary>
        public string Name { get; init; } = "";
        /// <summary>
        /// The backup description
        /// </summary>
        public string Description { get; init; } = "";
        /// <summary>
        /// The path to the local database
        /// </summary>
        public string? DBPath { get; init; } = null;
        /// <summary>
        /// The backup tags
        /// </summary>
        public string[]? Tags { get; init; }
        /// <summary>
        /// The backup target url
        /// </summary>
        public string TargetURL { get; init; } = "";

        /// <summary>
        /// The backup source folders and files
        /// </summary>
        public string[] Sources { get; init; } = [];

        /// <summary>
        /// The backup settings
        /// </summary>
        public IEnumerable<SettingInputDto>? Settings { get; init; }

        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        public IEnumerable<FilterInputDto>? Filters { get; init; }

        /// <summary>
        /// The backup metadata
        /// </summary>
        public IDictionary<string, string>? Metadata { get; init; }
    }

    /// <summary>
    /// The schedule DTO
    /// </summary>
    public sealed record ScheduleInputDto
    {
        /// <summary>
        /// The Schedule ID
        /// </summary>
        public long ID { get; init; }
        /// <summary>
        /// The tags that this schedule affects
        /// </summary>
        public string[]? Tags { get; init; }
        /// <summary>
        /// The time this schedule is based on
        /// </summary>
        public DateTime? Time { get; init; }
        /// <summary>
        /// How often the backup is repeated
        /// </summary>
        public string? Repeat { get; init; }
        /// <summary>
        /// The time this schedule was last executed
        /// </summary>
        public DateTime? LastRun { get; init; }
        /// <summary>
        /// The rule that is parsed to figure out when to run this backup next time
        /// </summary>
        public string? Rule { get; init; }
        /// <summary>
        /// The days that the backup is allowed to run
        /// </summary>
        public DayOfWeek[]? AllowedDays { get; init; }
    }

    /// <summary>
    /// The setting DTO
    /// </summary>
    public sealed record SettingInputDto
    {
        /// <summary>
        /// The filter expression
        /// </summary>
        public string? Filter { get; init; }
        /// <summary>
        /// The setting option
        /// </summary>
        public required string Name { get; init; }
        /// <summary>
        /// The setting value
        /// </summary>
        public string? Value { get; init; }
    }

    /// <summary>
    /// The filter DTO
    /// </summary>
    public sealed record FilterInputDto
    {
        /// <summary>
        /// The sort order
        /// </summary>
        public long Order { get; init; } = 0;

        /// <summary>
        /// True if the filter includes the items, false if it excludes
        /// </summary>
        public bool Include { get; init; }

        /// <summary>
        /// The filter expression.
        /// If the filter is a regular expression, it starts and ends with hard brackets [ ]
        /// </summary>
        public string? Expression { get; init; }
    }
}
