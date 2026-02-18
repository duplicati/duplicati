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

using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Server.Database
{
    /// <summary>
    /// Represents an additional target URL entry for a backup.
    /// Used by RemoteSynchronizationModule for remote sync destinations.
    /// </summary>
    public class TargetUrlEntry : ITargetUrlEntry
    {
        /// <summary>
        /// The database ID (internal use only)
        /// </summary>
        public long ID { get; set; }

        /// <summary>
        /// The backup ID this target URL belongs to
        /// </summary>
        public long BackupID { get; set; }

        /// <summary>
        /// The unique key for this target URL entry (UUID, user-facing identifier)
        /// </summary>
        public string TargetUrlKey { get; set; } = "";

        /// <summary>
        /// The target URL (encrypted when stored in database)
        /// </summary>
        public string TargetUrl { get; set; } = "";

        /// <summary>
        /// The synchronization mode: inline, interval, or counting
        /// </summary>
        public string Mode { get; set; } = "inline";

        /// <summary>
        /// The interval for interval mode (e.g., "1h", "30m")
        /// </summary>
        public string? Interval { get; set; }

        /// <summary>
        /// Additional options as a JSON object (auto-create-folders, backend-retries, etc.)
        /// </summary>
        public Dictionary<string, object>? Options { get; set; }

        /// <summary>
        /// The creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The last update timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; }
    }
}
