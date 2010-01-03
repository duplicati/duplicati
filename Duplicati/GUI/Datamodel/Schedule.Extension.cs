#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

        public void GetOptions(Dictionary<string, string> options)
        {
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
        
    }
}
