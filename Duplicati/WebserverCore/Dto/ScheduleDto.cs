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
/// The schedule DTO
/// </summary>
public sealed record ScheduleDto
{
    /// <summary>
    /// The Schedule ID
    /// </summary>
    public required long ID { get; init; }
    /// <summary>
    /// The tags that this schedule affects
    /// </summary>
    public required string[] Tags { get; init; }
    /// <summary>
    /// The time this schedule is based on
    /// </summary>
    public required DateTime Time { get; init; }
    /// <summary>
    /// How often the backup is repeated
    /// </summary>
    public required string Repeat { get; init; }
    /// <summary>
    /// The time this schedule was last executed
    /// </summary>
    public required DateTime LastRun { get; init; }
    /// <summary>
    /// The rule that is parsed to figure out when to run this backup next time
    /// </summary>
    public required string Rule { get; init; }
    /// <summary>
    /// The days that the backup is allowed to run
    /// </summary>
    public required DayOfWeek[] AllowedDays { get; init; }
}
