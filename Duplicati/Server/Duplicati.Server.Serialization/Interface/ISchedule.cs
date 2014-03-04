//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
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

