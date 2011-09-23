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
/// <metadata>
/// <creator>This class was created by DataClassFileBuilder (LightDatamodel)</creator>
/// <provider name="System.Data.LightDatamodel.SQLiteDataProvider" connectionstring="Version=3;Data Source=D:\Dokumenter\duplicati\Duplicati\GUI\Datamodel\Duplicati.sqlite;" />
/// <type>Table</type>
/// <namespace>Duplicati.Datamodel</namespace>
/// <name>Schedule</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("Schedule")]
	public partial class Schedule : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, Relation("TaskSchedule", typeof(Task), "ScheduleID", false), DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[DatabaseField("Name")]
		private System.String m_Name = "";
		[DatabaseField("Path")]
		private System.String m_Path = "";
		[DatabaseField("When")]
		private System.DateTime m_When = new System.DateTime(1, 1, 1);
		[DatabaseField("Repeat")]
		private System.String m_Repeat = "";
		[DatabaseField("Weekdays")]
		private System.String m_Weekdays = "";
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.String Name
		{
			get{return m_Name;}
			set{object oldvalue = m_Name;OnBeforeDataChange(this, "Name", oldvalue, value);m_Name = value;OnAfterDataChange(this, "Name", oldvalue, value);}
		}

		public System.String Path
		{
			get{return m_Path;}
			set{object oldvalue = m_Path;OnBeforeDataChange(this, "Path", oldvalue, value);m_Path = value;OnAfterDataChange(this, "Path", oldvalue, value);}
		}

		public System.DateTime When
		{
			get{return m_When;}
			set{object oldvalue = m_When;OnBeforeDataChange(this, "When", oldvalue, value);m_When = value;OnAfterDataChange(this, "When", oldvalue, value);}
		}

		public System.String Repeat
		{
			get{return m_Repeat;}
			set{object oldvalue = m_Repeat;OnBeforeDataChange(this, "Repeat", oldvalue, value);m_Repeat = value;OnAfterDataChange(this, "Repeat", oldvalue, value);}
		}

		public System.String Weekdays
		{
			get{return m_Weekdays;}
			set{object oldvalue = m_Weekdays;OnBeforeDataChange(this, "Weekdays", oldvalue, value);m_Weekdays = value;OnAfterDataChange(this, "Weekdays", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Task))]
		public Task Task
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Task>("TaskSchedule", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("TaskSchedule", this, value); }
		}

#endregion

	}

}