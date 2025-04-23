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
using Duplicati.Server.Serialization;

namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the server status DTO.
/// </summary>
public sealed record ServerStatusDto
{
    /// <summary>
    /// Gets or sets the active task.
    /// </summary>
    public required Tuple<long, string>? ActiveTask { get; init; }

    /// <summary>
    /// Gets or sets the state of the program.
    /// </summary>
    public required LiveControlState ProgramState { get; set; }

    /// <summary>
    /// Gets the IDs of the tasks in the scheduler queue.
    /// </summary>
    public required IList<Tuple<long, string>> SchedulerQueueIds { get; init; } = [];

    /// <summary>
    /// Gets or sets the proposed schedule.
    /// </summary>
    public required IList<Tuple<string, DateTime>> ProposedSchedule { get; init; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether there is a warning.
    /// </summary>
    public required bool HasWarning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there is an error.
    /// </summary>
    public required bool HasError { get; set; }

    /// <summary>
    /// Gets or sets the suggested status icon.
    /// </summary>
    public required SuggestedStatusIcon SuggestedStatusIcon { get; set; }

    /// <summary>
    /// Gets or sets the estimated end time of the pause.
    /// </summary>
    public required DateTime EstimatedPauseEnd { get; set; }

    /// <summary>
    /// Gets the ID of the last event.
    /// </summary>
    public required long LastEventID { get; init; }

    /// <summary>
    /// Gets the ID of the last data update.
    /// </summary>
    public required long LastDataUpdateID { get; init; }

    /// <summary>
    /// Gets the ID of the last notification update.
    /// </summary>
    public required long LastNotificationUpdateID { get; init; }

    /// <summary>
    /// Gets the updated version.
    /// </summary>
    public required string? UpdatedVersion { get; init; }

    /// <summary>
    /// Gets the state of the updater.
    /// </summary>
    public required UpdatePollerStates UpdaterState { get; init; }

    /// <summary>
    /// Gets or sets the download link for the update.
    /// </summary>
    public required string? UpdateDownloadLink { get; set; }

    /// <summary>
    /// Gets the progress of the update download.
    /// </summary>
    public required double UpdateDownloadProgress { get; init; }
}