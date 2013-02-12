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
/// <name>Log</name>
/// <sql></sql>
/// </metadata>

using System.Data.LightDatamodel;
using System.Data.LightDatamodel.DataClassAttributes;

namespace Duplicati.Datamodel
{

	[DatabaseTable("Log")]
	public partial class Log : DataClassBase
	{

#region " private members "

		[AutoIncrement, PrimaryKey, DatabaseField("ID")]
		private System.Int64 m_ID = long.MinValue;
		[Relation("LogTask", typeof(Task), "ID"), DatabaseField("TaskID")]
		private System.Int64 m_TaskID = long.MinValue;
		[DatabaseField("EndTime")]
		private System.DateTime m_EndTime = new System.DateTime(1, 1, 1);
		[DatabaseField("BeginTime")]
		private System.DateTime m_BeginTime = new System.DateTime(1, 1, 1);
		[DatabaseField("Action")]
		private System.String m_Action = "";
		[DatabaseField("SubAction")]
		private System.String m_SubAction = "";
		[DatabaseField("Transfersize")]
		private System.Int64 m_Transfersize = long.MinValue;
		[DatabaseField("ParsedStatus")]
		private System.String m_ParsedStatus = "";
        [DatabaseField("ParsedMessage")]
        private System.String m_ParsedMessage = "";
        [Relation("LogBlob", typeof(LogBlob), "ID"), DatabaseField("LogBlobID")]
		private System.Int64 m_LogBlobID = long.MinValue;
#endregion

#region " properties "

		public System.Int64 ID
		{
			get{return m_ID;}
			set{object oldvalue = m_ID;OnBeforeDataChange(this, "ID", oldvalue, value);m_ID = value;OnAfterDataChange(this, "ID", oldvalue, value);}
		}

		public System.Int64 TaskID
		{
			get{return m_TaskID;}
			set{object oldvalue = m_TaskID;OnBeforeDataChange(this, "TaskID", oldvalue, value);m_TaskID = value;OnAfterDataChange(this, "TaskID", oldvalue, value);}
		}

		public System.DateTime EndTime
		{
			get{return m_EndTime;}
			set{object oldvalue = m_EndTime;OnBeforeDataChange(this, "EndTime", oldvalue, value);m_EndTime = value;OnAfterDataChange(this, "EndTime", oldvalue, value);}
		}

		public System.DateTime BeginTime
		{
			get{return m_BeginTime;}
			set{object oldvalue = m_BeginTime;OnBeforeDataChange(this, "BeginTime", oldvalue, value);m_BeginTime = value;OnAfterDataChange(this, "BeginTime", oldvalue, value);}
		}

		public System.String Action
		{
			get{return m_Action;}
			set{object oldvalue = m_Action;OnBeforeDataChange(this, "Action", oldvalue, value);m_Action = value;OnAfterDataChange(this, "Action", oldvalue, value);}
		}

		public System.String SubAction
		{
			get{return m_SubAction;}
			set{object oldvalue = m_SubAction;OnBeforeDataChange(this, "SubAction", oldvalue, value);m_SubAction = value;OnAfterDataChange(this, "SubAction", oldvalue, value);}
		}

		public System.Int64 Transfersize
		{
			get{return m_Transfersize;}
			set{object oldvalue = m_Transfersize;OnBeforeDataChange(this, "Transfersize", oldvalue, value);m_Transfersize = value;OnAfterDataChange(this, "Transfersize", oldvalue, value);}
		}

		public System.String ParsedStatus
		{
			get{return m_ParsedStatus;}
			set{object oldvalue = m_ParsedStatus;OnBeforeDataChange(this, "ParsedStatus", oldvalue, value);m_ParsedStatus = value;OnAfterDataChange(this, "ParsedStatus", oldvalue, value);}
		}

        public System.String ParsedMessage
        {
            get { return m_ParsedMessage; }
            set { object oldvalue = m_ParsedMessage; OnBeforeDataChange(this, "ParsedMessage", oldvalue, value); m_ParsedMessage = value; OnAfterDataChange(this, "ParsedMessage", oldvalue, value); }
        }

		public System.Int64 LogBlobID
		{
			get{return m_LogBlobID;}
			set{object oldvalue = m_LogBlobID;OnBeforeDataChange(this, "LogBlobID", oldvalue, value);m_LogBlobID = value;OnAfterDataChange(this, "LogBlobID", oldvalue, value);}
		}

#endregion

#region " referenced properties "

		[Affects(typeof(Task))]
		public Task OwnerTask
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<Task>("LogTask", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("LogTask", this, value); }
		}

		[Affects(typeof(LogBlob))]
		public LogBlob Blob
		{
			get{ return ((DataFetcherWithRelations)m_dataparent).GetRelatedObject<LogBlob>("LogBlob", this); }
			set{ ((DataFetcherWithRelations)m_dataparent).SetRelatedObject("LogBlob", this, value); }
		}

#endregion

	}

}