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

namespace Duplicati.Server.Serialization.Interface
{
    /// <summary>
    /// A single schedule
    /// </summary>
    public interface ISchedule
    {
        /// <summary>
        /// The Schedule ID
        /// </summary>
        long ID { get; set; }
        /// <summary>
        /// The tags that this schedule affects
        /// </summary>
        string[] Tags { get; set; }
        /// <summary>
        /// The time this schedule is based on
        /// </summary>
        DateTime Time { get; set; }
        /// <summary>
        /// How often the backup is repeated
        /// </summary>
        string Repeat { get; set; }
        /// <summary>
        /// The time this schedule was last executed
        /// </summary>
        /// <value>The last run.</value>
        DateTime LastRun { get; set; }
        /// <summary>
        /// The rule that is parsed to figure out when to run this backup next time
        /// </summary>
        string Rule { get; set; }
        /// <summary>
        /// The days that the backup is allowed to run
        /// </summary>
        DayOfWeek[] AllowedDays { get; set; }
    }
}

