#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Datamodel
{
    public partial class Schedule
    {
        private DateTime m_nextSchedule = new DateTime();

        public bool ExistsInDb
        {
            get { return this.ID > 0; }
        }

        /// <summary>
        /// Gets or sets the next scheduled time.
        /// Note that this property is volatile, so
        /// commiting the object will not persist this value.
        /// Only a call to ScheduledRunCompleted will persist this value.
        /// </summary>
        public DateTime NextScheduledTime
        {
            get
            {
                if (m_nextSchedule.Ticks == 0)
                    return this.When;
                else
                    return m_nextSchedule;
            }
            set 
            {
                m_nextSchedule = value;
            }
        }

        /// <summary>
        /// Signals that the a scheduled run has completed.
        /// Will write a new scheduled time to the database
        /// </summary>
        public void ScheduledRunCompleted()
        {
            if (m_nextSchedule.Ticks != 0)
            {
                this.When = m_nextSchedule;
                m_nextSchedule = new DateTime();
                ((System.Data.LightDatamodel.IDataFetcherWithRelations)this.DataParent).CommitRecursiveWithRelations(this);
            }
        }

        /// <summary>
        /// Signals that the operation failed and that the backup
        /// </summary>
        public void ScheduledRunFailed()
        {
            m_nextSchedule = new DateTime(0);
        }

        /// <summary>
        /// Gets or sets the schedule allowed weekdays,
        /// this is a parser/wrapper for the Weekdays string
        /// </summary>
        public DayOfWeek[] AllowedWeekdays
        {
            get
            {
                if (string.IsNullOrEmpty(this.Weekdays))
                    return null;
                else
                {
                    string[] names = System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames;
                    List<DayOfWeek> l = new List<DayOfWeek>();
                    foreach(string s in this.Weekdays.Split(','))
                    {
                        int x = -1;
                        for(int i = 0; i < names.Length; i++)
                            if (s.Trim().Equals(names[i], StringComparison.InvariantCultureIgnoreCase))
                            {
                                x = i;
                                break;
                            }
                        
                        if (x >= 0 && !l.Contains((DayOfWeek)x))
                            l.Add((DayOfWeek)x);
                    }

                    l.Sort();
                    return l.ToArray();
                }
            }
            set
            {
                if (value == null || value.Length == 0)
                    this.Weekdays = null;
                else
                {
                    //Filter duplicates
                    List<DayOfWeek> l = new List<DayOfWeek>();
                    foreach (DayOfWeek d in value)
                        if (!l.Contains(d))
                            l.Add(d);

                    //If all is selected, don't store
                    if (l.Count == 7)
                        this.Weekdays = null;
                    else
                    {
                        l.Sort();

                        StringBuilder sb = new StringBuilder();
                        foreach (DayOfWeek d in l)
                        {
                            if (sb.Length != 0)
                                sb.Append(",");
                            sb.Append(System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames[(int)d]);
                        }

                        this.Weekdays = sb.ToString();
                    }
                }
            }
        }
        
    }
}
